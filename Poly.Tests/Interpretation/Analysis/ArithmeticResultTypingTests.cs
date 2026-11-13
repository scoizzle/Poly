using Poly.Ast.Nodes;
using Poly.Interpretation;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;
using Poly.Tests.Introspection;

namespace Poly.Tests.Interpretation.Analysis;

/// <summary>
/// T-17: the lowered-tree result type of an arithmetic node is the type provider's operator result
/// (<c>GetOperator</c>), not a private promotion. The pins that hold before and after are the characterisation (the
/// Text, char, bitwise-required and decimal shapes); the cells that move are named with the row that owns the rest.
/// </summary>
public class ArithmeticResultTypingTests {
    private static readonly OperatorKind[] ArithmeticKinds = [
        OperatorKind.Add, OperatorKind.Subtract, OperatorKind.Multiply, OperatorKind.Divide, OperatorKind.Modulo];

    private static readonly OperatorKind[] ComparisonKinds = [
        OperatorKind.Equal, OperatorKind.NotEqual, OperatorKind.LessThan, OperatorKind.LessThanOrEqual,
        OperatorKind.GreaterThan, OperatorKind.GreaterThanOrEqual];

    /// <summary>The numeric and char operands the compatibility analyzer does not re-stamp (no decimal, no string, no date).</summary>
    private static readonly Type[] Operands = [
        typeof(char), typeof(sbyte), typeof(byte), typeof(short), typeof(ushort), typeof(int), typeof(uint),
        typeof(long), typeof(ulong), typeof(float), typeof(double), typeof(nint), typeof(nuint)];

    private static ITypeDefinition Clr(Type type) => ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(type);

    private static Parameter Param(string name, Type type) => new(name, new ClrTypeReference(type));

    private static Type? Stamp(OperatorKind kind, Node left, Node? right, out Poly.Analysis.AnalysisResult analysis) {
        var node = NativeIntegerGrid.Build(kind, left, right);
        analysis = Interpreter.Analyze(node, ClrTypeDefinitionRegistry.Shared);
        return analysis.GetResolvedType(node)?.GetRuntimeType();
    }

    private static Type? Stamp(OperatorKind kind, Node left, Node? right) => Stamp(kind, left, right, out _);

    /// <summary>
    /// Every arithmetic cell over two parameters of the 13 numeric operands: the stamp is the provider's result, and a
    /// pair the provider defines no operator for has no stamp (the pass invents no type; T-18 rejects those cells).
    /// 845 cells; the provider answers 745 of them.
    /// </summary>
    [Test]
    public async Task ArithmeticStamp_OverNumericOperands_IsTheProvidersOperatorResult() {
        var mismatches = new List<string>();
        var (cells, answered) = (0, 0);
        foreach (var kind in ArithmeticKinds)
            foreach (var left in Operands)
                foreach (var right in Operands) {
                    cells++;
                    var expected = Clr(left).GetOperator(kind, Clr(right))?.Result.GetRuntimeType();
                    answered += expected is null ? 0 : 1;
                    var actual = Stamp(kind, Param("a", left), Param("b", right));
                    if (actual != expected)
                        mismatches.Add($"{left.Name} {kind} {right.Name}: provider {expected?.Name ?? "none"}, stamp {actual?.Name ?? "none"}");
                }
        await Assert.That(mismatches).IsEmpty();
        await Assert.That(cells).IsEqualTo(845);
        await Assert.That(answered).IsEqualTo(745);
    }

    /// <summary>The corrected small-integer shapes by name (the 150 non-native cells of the ledger rule are their closure).</summary>
    [Test]
    [Arguments(typeof(sbyte), typeof(sbyte), typeof(int))]
    [Arguments(typeof(byte), typeof(short), typeof(int))]
    [Arguments(typeof(ushort), typeof(char), typeof(int))]
    [Arguments(typeof(uint), typeof(int), typeof(long))]
    [Arguments(typeof(sbyte), typeof(uint), typeof(long))]
    [Arguments(typeof(nint), typeof(int), typeof(nint))]
    [Arguments(typeof(int), typeof(nint), typeof(nint))]
    [Arguments(typeof(nuint), typeof(byte), typeof(nuint))]
    [Arguments(typeof(char), typeof(char), typeof(int))]
    public async Task Add_OfTheseOperands_IsStampedWithC_sType(Type left, Type right, Type expected) =>
        await Assert.That(Stamp(OperatorKind.Add, Param("a", left), Param("b", right))).IsEqualTo(expected);

    /// <summary>UInt64 with a signed operand: C# defines no operator, so the pass states no type (T-18 rejects the cell).</summary>
    [Test]
    [Arguments(typeof(ulong), typeof(int))]
    [Arguments(typeof(long), typeof(ulong))]
    [Arguments(typeof(ulong), typeof(sbyte))]
    public async Task Add_UInt64WithASignedOperand_HasNoStamp_AndIsRejected(Type left, Type right) {
        var stamp = Stamp(OperatorKind.Add, Param("a", left), Param("b", right), out var analysis);
        await Assert.That(stamp).IsNull();
        await Assert.That(analysis.Diagnostics.Any(static d => d.Severity == Poly.Analysis.DiagnosticSeverity.Error)).IsTrue();
    }

    /// <summary>Characterisation (green before and after): char arithmetic is int.</summary>
    [Test]
    public async Task CharPlusChar_IsInt32_And195() {
        var node = new Add(new Constant('a'), new Constant('b'));
        var analysis = Interpreter.Analyze(node, ClrTypeDefinitionRegistry.Shared);
        await Assert.That(analysis.GetResolvedType(node)?.GetRuntimeType()).IsEqualTo(typeof(int));
        using var exec = Interpreter.Execute(Interpreter.Compile(node, analysis));
        await Assert.That(exec.GetValue<int>()).IsEqualTo(195);
    }

    /// <summary>Characterisation (green before and after): a string operand makes Add a String; the Text + X policy is T-07's.</summary>
    [Test]
    [Arguments(typeof(string), typeof(string))]
    [Arguments(typeof(string), typeof(long))]
    [Arguments(typeof(long), typeof(string))]
    [Arguments(typeof(string), typeof(int?))]
    [Arguments(typeof(string), typeof(bool))]
    public async Task Add_WithAStringOperand_IsString(Type left, Type right) =>
        await Assert.That(Stamp(OperatorKind.Add, Param("a", left), Param("b", right))).IsEqualTo(typeof(string));

    /// <summary>
    /// Lock amendment 9: <c>Text + unresolved</c> was String on master (any string operand won); an unresolved operand
    /// now gives no stamp. Declared: no cell the DSL accepts reaches it.
    /// </summary>
    [Test]
    public async Task Add_TextWithAnUnresolvedOperand_HasNoStamp_Declared() {
        var unresolved = new Default(null);
        await Assert.That(Stamp(OperatorKind.Add, new Constant("a"), unresolved)).IsNull();
        await Assert.That(Stamp(OperatorKind.Add, unresolved, new Constant("a"))).IsNull();
    }

    /// <summary>Characterisation (green before and after): decimal beside its own type and the integers.</summary>
    [Test]
    [Arguments(typeof(decimal), typeof(decimal), typeof(decimal))]
    [Arguments(typeof(decimal), typeof(int), typeof(decimal))]
    [Arguments(typeof(long), typeof(decimal), typeof(decimal))]
    [Arguments(typeof(decimal?), typeof(decimal), typeof(decimal?))]
    public async Task Multiply_WithDecimal_IsDecimal(Type left, Type right, Type expected) =>
        await Assert.That(Stamp(OperatorKind.Multiply, Param("a", left), Param("b", right))).IsEqualTo(expected);

    /// <summary>Nullable operands lift the provider's result of the underlying types (kept in this pass until T-06).</summary>
    [Test]
    [Arguments(typeof(int?), typeof(long), typeof(long?))]
    [Arguments(typeof(long?), typeof(long), typeof(long?))]
    [Arguments(typeof(int?), typeof(long?), typeof(long?))]
    [Arguments(typeof(sbyte?), typeof(sbyte), typeof(int?))]
    [Arguments(typeof(uint?), typeof(int), typeof(long?))]
    [Arguments(typeof(decimal?), typeof(decimal?), typeof(decimal?))]
    [Arguments(typeof(string), typeof(int?), typeof(string))]
    public async Task Add_WithANullableOperand_IsTheLiftedProviderResult(Type left, Type right, Type expected) =>
        await Assert.That(Stamp(OperatorKind.Add, Param("a", left), Param("b", right))).IsEqualTo(expected);

    /// <summary>
    /// T-22a: required bitwise and shift operands are stamped with the provider's result, as C# types them (they kept the
    /// left type before); a shift count of a wider integer type is Poly's policy, typed by the left operand.
    /// </summary>
    [Test]
    [Arguments(OperatorKind.BitwiseOr, typeof(long), typeof(int), typeof(long))]
    [Arguments(OperatorKind.BitwiseAnd, typeof(sbyte), typeof(sbyte), typeof(int))]
    [Arguments(OperatorKind.BitwiseXor, typeof(uint), typeof(int), typeof(long))]
    [Arguments(OperatorKind.ShiftLeft, typeof(int), typeof(long), typeof(int))]
    [Arguments(OperatorKind.ShiftLeft, typeof(byte), typeof(ulong), typeof(int))]
    [Arguments(OperatorKind.ShiftRight, typeof(ulong), typeof(int), typeof(ulong))]
    public async Task BitwiseAndShift_OfRequiredOperands_AreTheProvidersResult(OperatorKind kind, Type left, Type right, Type expected) =>
        await Assert.That(Stamp(kind, Param("a", left), Param("b", right))).IsEqualTo(expected);

    /// <summary>
    /// The nullable bitwise and shift branch is judged by the node's own operator (lock amendment 7). Two named cells
    /// change from master: <c>Int32? &lt;&lt; Int64</c> was <c>Int64?</c> and is <c>Int32?</c> (the shift-count policy:
    /// the left operand types a shift), <c>UInt64? | Int32</c> was <c>UInt64?</c> and has none (C# CS0034).
    /// </summary>
    [Test]
    [Arguments(OperatorKind.ShiftLeft, typeof(int?), typeof(int), typeof(int?))]
    [Arguments(OperatorKind.ShiftRight, typeof(long?), typeof(int), typeof(long?))]
    [Arguments(OperatorKind.BitwiseOr, typeof(long), typeof(int?), typeof(long?))]
    [Arguments(OperatorKind.BitwiseAnd, typeof(int?), typeof(int?), typeof(int?))]
    [Arguments(OperatorKind.ShiftLeft, typeof(int?), typeof(long), typeof(int?))]
    [Arguments(OperatorKind.ShiftLeft, typeof(int), typeof(long?), typeof(int?))]
    [Arguments(OperatorKind.BitwiseOr, typeof(ulong?), typeof(int), null)]
    public async Task BitwiseAndShift_WithANullableOperand_AreJudgedAsTheirOwnOperator(OperatorKind kind, Type left, Type right, Type? expected) =>
        await Assert.That(Stamp(kind, Param("a", left), Param("b", right))).IsEqualTo(expected);

    /// <summary>
    /// Lock amendment 6: the fixed <c>bool</c> arm of a comparison node equals the provider's result wherever the provider
    /// answers, so a provider answer that stops being <c>bool</c> fails here. The provider gives no answer for enum and
    /// null comparison (T-25, T-06), which is why the arm stays fixed; reference equality it answers since T-22a.
    /// </summary>
    [Test]
    public async Task ComparisonStamp_IsBool_WhereverTheProviderAnswers() {
        var mismatches = new List<string>();
        var answered = 0;
        foreach (var kind in ComparisonKinds)
            foreach (var left in Operands)
                foreach (var right in Operands) {
                    var resolution = Clr(left).GetOperator(kind, Clr(right));
                    if (resolution is null)
                        continue;
                    answered++;
                    var stamp = Stamp(kind, Param("a", left), Param("b", right));
                    if (resolution.Value.Result.GetRuntimeType() != typeof(bool) || stamp != typeof(bool))
                        mismatches.Add($"{left.Name} {kind} {right.Name}: provider {resolution.Value.Result.GetRuntimeType()?.Name}, stamp {stamp?.Name ?? "none"}");
                }
        await Assert.That(mismatches).IsEmpty();
        await Assert.That(answered).IsGreaterThan(0);
    }

    /// <summary>Equality over operands the provider answers nothing for keeps its fixed bool arm.</summary>
    [Test]
    public async Task Equality_OverEnumNullAndReferences_IsStillBool() {
        await Assert.That(Stamp(OperatorKind.Equal, new Constant(DayOfWeek.Monday), new Constant(DayOfWeek.Friday))).IsEqualTo(typeof(bool));
        await Assert.That(Stamp(OperatorKind.Equal, Param("a", typeof(object)), new Constant(null))).IsEqualTo(typeof(bool));
        await Assert.That(Stamp(OperatorKind.NotEqual, Param("a", typeof(string)), Param("b", typeof(string)))).IsEqualTo(typeof(bool));
    }

    /// <summary>
    /// Lock amendment 10: operands the provider defines no arithmetic for have no stamp (master stamped the left operand's
    /// type): an object, a null constant, an enum with a number, a boolean pair. A CLR-defined temporal operator is stamped
    /// with the operator's result (<c>DateTime - DateTime</c> is TimeSpan; master said DateTime), and the verdict is
    /// unchanged.
    /// </summary>
    [Test]
    public async Task Arithmetic_OverOperandsTheProviderDoesNotDefine_HasNoStamp() {
        await Assert.That(Stamp(OperatorKind.Add, new Constant(null), new Constant(1))).IsNull();
        await Assert.That(Stamp(OperatorKind.Add, Param("a", typeof(object)), Param("b", typeof(int)))).IsNull();
        await Assert.That(Stamp(OperatorKind.Add, new Constant(DayOfWeek.Monday), new Constant(1))).IsNull();
        await Assert.That(Stamp(OperatorKind.Add, Param("a", typeof(bool)), Param("b", typeof(bool)))).IsNull();
    }

    [Test]
    public async Task DateTimeMinusDateTime_IsStampedTimeSpan_AndTheVerdictIsUnchanged() {
        var stamp = Stamp(OperatorKind.Subtract, Param("a", typeof(DateTime)), Param("b", typeof(DateTime)), out var analysis);
        await Assert.That(stamp).IsEqualTo(typeof(TimeSpan));
        await Assert.That(analysis.Diagnostics.Any(static d => d.Severity == Poly.Analysis.DiagnosticSeverity.Error)).IsTrue();
    }

    /// <summary>
    /// The shapes the shape-reading tests execute (lock amendment 8), with their values: a ulong with a non-negative int
    /// constant is the T-18 constant exception (ConstantOperandRule, removed by T-26), so it stays UInt64; a byte and a
    /// short give Int32.
    /// </summary>
    [Test]
    public async Task ShapesTheOtherTestsExecute_StayAccepted_AndRunRight() {
        var ulongAndInt = new Add(new Constant(100UL), new Constant(50));
        using (var exec = Interpreter.Execute(Interpreter.Compile(ulongAndInt)))
            await Assert.That(exec.GetValue<ulong>()).IsEqualTo(150UL);
        var byteAndShort = new Add(new Constant((byte)5), new Constant((short)7));
        var analysis = Interpreter.Analyze(byteAndShort, ClrTypeDefinitionRegistry.Shared);
        await Assert.That(analysis.GetResolvedType(byteAndShort)?.GetRuntimeType()).IsEqualTo(typeof(int));
        using (var exec = Interpreter.Execute(Interpreter.Compile(byteAndShort, analysis)))
            await Assert.That(exec.GetValue<int>()).IsEqualTo(12);
    }

    /// <summary>Lock amendment 10: an entity (a CLR class with no operator) beside a number has no stamp; master stamped the left operand's type.</summary>
    [Test]
    public async Task Add_EntityWithANumber_HasNoStamp() =>
        await Assert.That(Stamp(OperatorKind.Add, Param("e", typeof(LoweredTreeOperandCoverageTests.Item)), Param("n", typeof(long)))).IsNull();

    /// <summary>
    /// The date-offset stamps: the offset is stamped with the date type the compatibility rewrite produces (one shared rule,
    /// T-10), the nullable left keeps its lift, and a non-nullable left beside a decimal has no stamp (the provider defines
    /// no operator and the analysis rejects it).
    /// </summary>
    [Test]
    [Arguments(OperatorKind.Add, typeof(DateTime), typeof(int), typeof(DateTime))]
    [Arguments(OperatorKind.Subtract, typeof(DateOnly), typeof(long), typeof(DateOnly))]
    [Arguments(OperatorKind.Add, typeof(DateTime?), typeof(int), typeof(DateTime?))]
    [Arguments(OperatorKind.Add, typeof(DateTime), typeof(decimal), null)]
    [Arguments(OperatorKind.Subtract, typeof(DateOnly), typeof(decimal), null)]
    [Arguments(OperatorKind.Add, typeof(DateTime?), typeof(decimal), typeof(DateTime?))]
    public async Task DateOffset_IsStampedWithTheDateType(OperatorKind kind, Type left, Type right, Type? expected) =>
        await Assert.That(Stamp(kind, Param("d", left), Param("n", right))).IsEqualTo(expected);

    /// <summary>
    /// T-22a (lock amendment 7, red test 2): arithmetic over an operand the provider defines no operator for (an
    /// <c>object</c>, an entity, a struct) is refused at the node, as C# refuses it, in every position. Master refused it
    /// only as an argument the left operand's type did not fit (<c>TakesInt(obj + 1)</c>) and computed it at the root, as
    /// an <c>object</c> argument and as a returned value; T-17's <c>UndefinedArithmeticMatch</c> kept that and is deleted.
    /// </summary>
    [Test]
    [Arguments(typeof(object), "Object")]
    [Arguments(typeof(LoweredTreeOperandCoverageTests.Item), "Item")]
    [Arguments(typeof(KeyValuePair<int, int>), "KeyValuePair`2")]
    public async Task UndefinedArithmetic_IsRefusedAtTheNode_InEveryPosition(Type operand, string name) {
        Node Sum() => new Add(Param("o", operand), new Constant(1));
        Node Call(string method) => new Invoke(new Member(new ClrTypeReference(typeof(TypedCalls)), method), Sum());
        foreach (var node in new[] { Sum(), Call(nameof(TypedCalls.TakesInt)), Call(nameof(TypedCalls.TakesObject)), new Block([new Return(Sum())]) }) {
            var analysis = Interpreter.Analyze(node, ClrTypeDefinitionRegistry.Shared);
            await Assert.That(analysis.Diagnostics.Where(static d => d.Severity == Poly.Analysis.DiagnosticSeverity.Error).Select(static d => d.Message).ToArray())
                .IsEquivalentTo([$"arithmetic operand is not numeric (got '{name}' and 'Int32'): no Add operator for these types"]);
        }
    }

    private static string Run(Node node, params object?[] args) {
        var analysis = Interpreter.Analyze(node, ClrTypeDefinitionRegistry.Shared);
        if (analysis.Diagnostics.FirstOrDefault(static d => d.Severity == Poly.Analysis.DiagnosticSeverity.Error) is { } error)
            return "rejected: " + error.Message;
        var program = Interpreter.Compile(node, analysis);
        using var exec = Interpreter.Execute(program, args);
        return $"{analysis.GetResolvedType(node)?.GetRuntimeType()?.Name}; {NativeIntegerGrid.Format(NativeIntegerGrid.Read(exec, program.RootClrType))}";
    }

    /// <summary>
    /// T-22a, lock amendment 4: the shapes that must stay accepted when the scalar gate is replaced by the operand's type
    /// definition, each with the type and value C# gives it (the value master computed). A shift by a <c>long</c> count is
    /// Poly's policy: the count converted to int unchecked (33 is 1).
    /// </summary>
    [Test]
    public async Task MustStayAccepted_ComputeAsCSharp() {
        var item = new LoweredTreeOperandCoverageTests.Item(1m);
        await Assert.That(Run(new Add(Param("o", typeof(object)), new Constant("a")), "x")).IsEqualTo("String; xa");
        await Assert.That(Run(new Add(Param("e", typeof(LoweredTreeOperandCoverageTests.Item)), new Constant("a")), item)).EndsWith("Itema");
        await Assert.That(Run(new Equal(Param("a", typeof(long[])), Param("b", typeof(long[]))), new long[] { 1 }, new long[] { 1 })).IsEqualTo("Boolean; False");
        await Assert.That(Run(new Equal(Param("a", typeof(List<int>)), new Constant(null)), new List<int>())).IsEqualTo("Boolean; False");
        await Assert.That(Run(new BitwiseAnd(Param("a", typeof(ulong)), new Constant(1)), ulong.MaxValue)).IsEqualTo("UInt64; 1");
        await Assert.That(Run(new BitwiseAnd(Param("a", typeof(bool)), Param("b", typeof(bool))), true, false)).IsEqualTo("Boolean; False");
        await Assert.That(Run(new BitwiseAnd(Param("a", typeof(int)), Param("b", typeof(long))), -1, 4294967296L)).IsEqualTo("Int64; 4294967296");
        await Assert.That(Run(new ShiftLeft(Param("a", typeof(int)), Param("b", typeof(int))), 1, 33)).IsEqualTo("Int32; 2");
        await Assert.That(Run(new ShiftLeft(Param("a", typeof(int)), Param("b", typeof(long))), 1, 33L)).IsEqualTo("Int32; 2");
        await Assert.That(Run(new ShiftRight(Param("a", typeof(long)), Param("b", typeof(uint))), -64L, 3u)).IsEqualTo("Int64; -8");
        await Assert.That(Run(new ShiftLeft(new Constant(1L), new Constant(3L)))).IsEqualTo("Int64; 8");
    }

    /// <summary>
    /// T-22a, lock amendment 8: <c>uint x &amp; 1</c> is Int64 (C# says uint through its constant conversion, T-26), with the
    /// value master computed; <c>uint x | -1</c> is Int64 -1 as in C# (master computed 4294967295).
    /// </summary>
    [Test]
    public async Task UInt32WithAnIntConstant_IsInt64() {
        await Assert.That(Run(new BitwiseAnd(Param("a", typeof(uint)), new Constant(1)), uint.MaxValue)).IsEqualTo("Int64; 1");
        await Assert.That(Run(new BitwiseOr(Param("a", typeof(uint)), new Constant(-1)), 5u)).IsEqualTo("Int64; -1");
    }

    public sealed class Target {
        public void M() { }
        public void N() { }
        public int F() => 1;
        public int G() => 2;
    }

    /// <summary>
    /// C# 12.12.9: <c>==</c> and <c>!=</c> over two operands of one delegate type compare invocation lists. The analysis
    /// accepts them and the VM computes C#'s value for the same instance, two distinct instances of the same target and
    /// method, and two different delegates (the base ran all twelve cells right; T-22a's first head refused them).
    /// </summary>
    [Test]
    public async Task DelegateEquality_RunsAsCSharp() {
        var t = new Target();
        Action same = t.M;
        Func<int> sameF = t.F;
        foreach (var (type, a, b) in new (Type, Delegate, Delegate)[] {
                     (typeof(Action), same, same), (typeof(Action), new Action(t.M), new Action(t.M)), (typeof(Action), new Action(t.M), new Action(t.N)),
                     (typeof(Func<int>), sameF, sameF), (typeof(Func<int>), new Func<int>(t.F), new Func<int>(t.F)), (typeof(Func<int>), new Func<int>(t.F), new Func<int>(t.G)) }) {
            await Assert.That(Run(new Equal(Param("a", type), Param("b", type)), a, b)).IsEqualTo($"Boolean; {a == b}");
            await Assert.That(Run(new NotEqual(Param("a", type), Param("b", type)), a, b)).IsEqualTo($"Boolean; {a != b}");
        }
    }

    public static class TypedCalls {
        public static int TakesInt(int value) => value;
        public static object TakesObject(object value) => value;
    }
}