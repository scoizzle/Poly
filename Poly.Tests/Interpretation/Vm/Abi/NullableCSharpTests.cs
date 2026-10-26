using Poly.Interpretation;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.Vm;
using Poly.Interpretation.Vm.Abi;
using Poly.Introspection;

namespace Poly.Tests.Interpretation.Vm.Abi;

/// <summary>
/// C# nullable semantics on the VM. A required value type is never null, so
/// comparing it with null is a constant after the operand runs. An optional
/// value lifts, and binary numeric promotion is C#'s: <c>ulong</c> stays
/// unsigned, <c>decimal</c> takes an integral, and a pair C# rejects fails closed.
/// </summary>
public class NullableCSharpTests {
    private static ExecutionResult Execute(Node body, params object?[] args) =>
        Interpreter.Execute(Interpreter.Compile(body), s => s.SetArgs(args));

    private static long Run(Node body, params object?[] args) {
        using var exec = Execute(body, args);
        return exec.RawValue;
    }

    private static ITypeDefinitionProvider Analyze(TypeDefinitionNode typeNode) {
        var analyzer = new TypeDefinitionNodeAnalyzer();
        var context = AnalysisContext.CreateDefault();
        analyzer.Analyze(context, typeNode);
        return context.TypeDefinitions;
    }

    private enum Color { Red = 1, Blue = 2 }

    [Test]
    public async Task RequiredLong_ComparedWithNull_IsConstant() {
        var age = new Parameter("age", TypeReference.To<long>());
        await Assert.That(Run(new NotEqual(age, new Constant(null)), 41L)).IsEqualTo(1L);
        await Assert.That(Run(new NotEqual(age, new Constant(null)), 0L)).IsEqualTo(1L);
        await Assert.That(Run(new NotEqual(age, new Constant(null)))).IsEqualTo(1L);
        await Assert.That(Run(new Equal(age, new Constant(null)), 41L)).IsEqualTo(0L);
        await Assert.That(Run(new NotEqual(new Constant(null), age), 41L)).IsEqualTo(1L);
    }

    [Test]
    public async Task RequiredLong_NotEqualNull_EmitsNoHeapDeref() {
        var age = new Parameter("age", TypeReference.To<long>());
        var node = new NotEqual(age, new Constant(null));
        var built = AbiEmitter.Build(node, Interpreter.Analyze(node), CompilationMode.NoDebug);
        var scan = VmTree.Read(built.Expression);
        await Assert.That(scan.HeapDerefs.Count).IsEqualTo(0);
    }

    [Test]
    public async Task RequiredDecimal_ComparedWithNull_IsConstant() {
        var amount = new Parameter("amount", TypeReference.To<decimal>());
        await Assert.That(Run(new Equal(amount, new Constant(null)), 5m)).IsEqualTo(0L);
        await Assert.That(Run(new NotEqual(amount, new Constant(null)), 0m)).IsEqualTo(1L);
        await Assert.That(Run(new NotEqual(amount, new Constant(null)))).IsEqualTo(1L);
    }

    [Test]
    public async Task RequiredDecimal_NotEqualNull_EmitsNoHeapDeref() {
        var amount = new Parameter("amount", TypeReference.To<decimal>());
        var node = new NotEqual(amount, new Constant(null));
        var built = AbiEmitter.Build(node, Interpreter.Analyze(node), CompilationMode.NoDebug);
        var scan = VmTree.Read(built.Expression);
        await Assert.That(scan.HeapDerefs.Count).IsEqualTo(0);
    }

    [Test]
    public async Task OptionalLong_UnaryMinus_Present_IsNegated() {
        var age = new Parameter("age", new OptionalTypeReference(TypeReference.To<long>()));
        using var exec = Execute(new UnaryMinus(age), 41L);
        await Assert.That(exec.GetValue<long>()).IsEqualTo(-41L);
    }

    [Test]
    public async Task OptionalLong_UnaryMinus_Absent_IsNull() {
        var age = new Parameter("age", new OptionalTypeReference(TypeReference.To<long>()));
        using var exec = Execute(new UnaryMinus(age));
        await Assert.That(exec.Result.Value).IsNull();
    }

    [Test]
    public async Task OptionalLong_BitwiseOr_Present_UsesValue() {
        var age = new Parameter("age", new OptionalTypeReference(TypeReference.To<long>()));
        using var exec = Execute(new BitwiseOr(new Constant(1L), age), 2L);
        await Assert.That(exec.GetValue<long>()).IsEqualTo(3L);
    }

    [Test]
    public async Task OptionalLong_BitwiseOr_Absent_IsNull() {
        var age = new Parameter("age", new OptionalTypeReference(TypeReference.To<long>()));
        using var exec = Execute(new BitwiseOr(new Constant(1L), age));
        await Assert.That(exec.Result.Value).IsNull();
    }

    [Test]
    public async Task OptionalBoolLocal_AssignScalar_StaysPresent() {
        var flag = new Parameter("flag", new OptionalTypeReference(TypeReference.To<bool>()));
        var local = new Variable("local");
        Node Body(Node scalar) => new Block([
            new Assignment(local, flag),
            new Assignment(local, scalar),
            local
        ], [local]);
        using var present = Execute(Body(new Equal(new Constant(1L), new Constant(1L))), true);
        await Assert.That(present.Result.Value is true).IsTrue();
        using var absent = Execute(Body(new Equal(new Constant(1L), new Constant(2L))), true);
        await Assert.That(absent.Result.Value is false).IsTrue();
    }

    private sealed record OptionalBag(long? Limit);

    [Test]
    public async Task OptionalMember_Null_EqualsNull_NotTheValue() {
        await Assert.That(Run(new Equal(
            new Member(new Constant(new OptionalBag(null)), "Limit"),
            new Constant(null)))).IsEqualTo(1L);
        await Assert.That(Run(new Equal(
            new Member(new Constant(new OptionalBag(null)), "Limit"),
            new Constant(41L)))).IsEqualTo(0L);
    }

    [Test]
    public async Task Constructor_OptionalScalarMember_ScalarZero_StoresZero() {
        var types = Analyze(new TypeDefinitionNode(
            "ZeroLot", "Branch",
            Constructors: [
                new ConstructorDefinitionNode(
                    [],
                    new Block([
                        new Assignment(new Member(new ThisReference(), "Limit"), new Constant(0L))
                    ]))
            ],
            Properties: [
                new PropertyDefinitionNode("Limit", new OptionalTypeReference(TypeReference.To<long>()))
            ]));
        using var exec = Interpreter.Execute(
            Interpreter.Compile(new New(new TypeReference("Branch.ZeroLot")), types));
        var bag = exec.GetValue<Dictionary<string, object?>>();
        await Assert.That(bag!["Limit"]).IsEqualTo(0L);
    }

    [Test]
    public async Task AstProperty_OptionalScalar_Absent_ReadsAsNull() {
        var types = Analyze(new TypeDefinitionNode(
            "BagLot", "Branch",
            Constructors: [new ConstructorDefinitionNode([], new Block([]))],
            Properties: [
                new PropertyDefinitionNode("Limit", new OptionalTypeReference(TypeReference.To<long>()))
            ],
            Methods: [
                new MethodDefinitionNode(
                    "Probe",
                    new PrimitiveTypeReference(PrimitiveType.Boolean),
                    Body: new Return(new Equal(
                        new Member(new ThisReference(), "Limit"), new Constant(null))))
            ]));
        using var exec = Interpreter.Execute(Interpreter.Compile(
            new Invoke(new Member(new New(new TypeReference("Branch.BagLot")), "Probe")),
            types));
        await Assert.That(exec.GetValue<bool>()).IsTrue();
    }

    [Test]
    public async Task OptionalDecimal_PlusOne_UsesTheValue() {
        var amount = new Parameter("amount", new OptionalTypeReference(TypeReference.To<decimal>()));
        using var present = Execute(new Add(amount, new Constant(1m)), 5m);
        await Assert.That(present.GetValue<decimal>()).IsEqualTo(6m);
        using var absent = Execute(new Add(amount, new Constant(1m)));
        await Assert.That(absent.Result.Value).IsNull();
    }

    [Test]
    public async Task OptionalULong_DividedByTwo_IsUnsigned() {
        var n = new Parameter("n", new OptionalTypeReference(TypeReference.To<ulong>()));
        using var exec = Execute(new Divide(n, new Constant(2UL)), ulong.MaxValue);
        await Assert.That(exec.Result.Value).IsEqualTo(ulong.MaxValue / 2);
    }

    [Test]
    public async Task OptionalULong_ShiftRight_IsLogical() {
        var n = new Parameter("n", new OptionalTypeReference(TypeReference.To<ulong>()));
        using var exec = Execute(new ShiftRight(n, new Constant(1)), ulong.MaxValue);
        await Assert.That(exec.Result.Value).IsEqualTo(ulong.MaxValue >> 1);
    }

    [Test]
    public async Task Int_EqualDecimal_ConvertsEitherOrder() {
        var count = new Parameter("count", TypeReference.To<int>());
        var amount = new Parameter("amount", TypeReference.To<decimal>());
        await Assert.That(Run(new Equal(count, amount), 4, 4.0m)).IsEqualTo(1L);
        await Assert.That(Run(new Equal(amount, count), 4.0m, 4)).IsEqualTo(1L);
        await Assert.That(Run(new Equal(count, amount), 4, 4.5m)).IsEqualTo(0L);
        await Assert.That(Run(new Equal(amount, count), 4.5m, 4)).IsEqualTo(0L);
    }

    [Test]
    public async Task OptionalInt_EqualOptionalDecimal_ConvertsAndLifts() {
        var count = new Parameter("count", new OptionalTypeReference(TypeReference.To<int>()));
        var amount = new Parameter("amount", new OptionalTypeReference(TypeReference.To<decimal>()));
        await Assert.That(Run(new Equal(count, amount), 4, 4.0m)).IsEqualTo(1L);
        await Assert.That(Run(new Equal(amount, count), 4.5m, 4)).IsEqualTo(0L);
        await Assert.That(Run(new Equal(count, amount))).IsEqualTo(1L);
        await Assert.That(Run(new Equal(count, amount), null, 4.0m)).IsEqualTo(0L);
    }

    [Test]
    public async Task OptionalDecimal_Equal_UsesTheValue() {
        var left = new Parameter("left", new OptionalTypeReference(TypeReference.To<decimal>()));
        var right = new Parameter("right", new OptionalTypeReference(TypeReference.To<decimal>()));
        await Assert.That(Run(new Equal(left, right), 1.5m, 1.5m)).IsEqualTo(1L);
        await Assert.That(Run(new Equal(left, right), 1.5m, 1.9m)).IsEqualTo(0L);
        await Assert.That(Run(new Equal(left, right))).IsEqualTo(1L);
    }

    [Test]
    public async Task OptionalDateTime_Equal_UsesTheValue() {
        var left = new Parameter("left", new OptionalTypeReference(TypeReference.To<DateTime>()));
        var right = new Parameter("right", new OptionalTypeReference(TypeReference.To<DateTime>()));
        var day = new DateTime(2026, 1, 1);
        await Assert.That(Run(new Equal(left, right), day, day)).IsEqualTo(1L);
        await Assert.That(Run(new Equal(left, right), day, day.AddDays(1))).IsEqualTo(0L);
        await Assert.That(Run(new Equal(left, right))).IsEqualTo(1L);
    }

    [Test]
    public async Task OptionalTimeSpan_Equal_UsesTheValue() {
        var left = new Parameter("left", new OptionalTypeReference(TypeReference.To<TimeSpan>()));
        var right = new Parameter("right", new OptionalTypeReference(TypeReference.To<TimeSpan>()));
        var span = TimeSpan.FromMinutes(5);
        await Assert.That(Run(new Equal(left, right), span, span)).IsEqualTo(1L);
        await Assert.That(Run(new Equal(left, right), span, TimeSpan.FromMinutes(6))).IsEqualTo(0L);
    }

    [Test]
    public async Task OptionalEnum_Equal_UsesTheValue() {
        var left = new Parameter("left", new OptionalTypeReference(TypeReference.To<Color>()));
        var right = new Parameter("right", new OptionalTypeReference(TypeReference.To<Color>()));
        await Assert.That(Run(new Equal(left, right), Color.Red, Color.Red)).IsEqualTo(1L);
        await Assert.That(Run(new Equal(left, right), Color.Red, Color.Blue)).IsEqualTo(0L);
        await Assert.That(Run(new Equal(left, new Constant(null)))).IsEqualTo(1L);
        await Assert.That(Run(new Equal(left, new Constant(Color.Red)))).IsEqualTo(0L);
        await Assert.That(Run(new Equal(left, new Constant(Color.Red)), Color.Red)).IsEqualTo(1L);
    }

    [Test]
    public async Task Bool_Ordering_IsRejected() {
        var flag = new Parameter("flag", TypeReference.To<bool>());
        await Assert.That(() => Interpreter.Compile(new GreaterThan(flag, new Constant(true))))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task RequiredLong_OrderingAgainstNull_IsRejected() {
        var age = new Parameter("age", TypeReference.To<long>());
        await Assert.That(() => Interpreter.Compile(new GreaterThan(age, new Constant(null))))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task OptionalDecimal_PlusDouble_IsRejected() {
        var amount = new Parameter("amount", new OptionalTypeReference(TypeReference.To<decimal>()));
        await Assert.That(() => Interpreter.Compile(new Add(amount, new Constant(1.0d))))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task OptionalULong_PlusSigned_IsRejected() {
        var wide = new Parameter("wide", new OptionalTypeReference(TypeReference.To<ulong>()));
        var narrow = new Parameter("narrow", TypeReference.To<int>());
        await Assert.That(() => Interpreter.Compile(new Add(wide, narrow)))
            .Throws<InvalidOperationException>();
    }
}