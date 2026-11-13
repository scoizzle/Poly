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

    /// <summary>Required bitwise and shift operands keep the left type (T-22 removes that rule; this row leaves it byte for byte).</summary>
    [Test]
    [Arguments(OperatorKind.BitwiseOr, typeof(long), typeof(int), typeof(long))]
    [Arguments(OperatorKind.BitwiseAnd, typeof(sbyte), typeof(sbyte), typeof(sbyte))]
    [Arguments(OperatorKind.BitwiseXor, typeof(uint), typeof(int), typeof(uint))]
    [Arguments(OperatorKind.ShiftLeft, typeof(int), typeof(long), typeof(int))]
    [Arguments(OperatorKind.ShiftRight, typeof(ulong), typeof(int), typeof(ulong))]
    public async Task BitwiseAndShift_OfRequiredOperands_KeepTheLeftType(OperatorKind kind, Type left, Type right, Type expected) =>
        await Assert.That(Stamp(kind, Param("a", left), Param("b", right))).IsEqualTo(expected);

    /// <summary>
    /// The nullable bitwise and shift branch is judged by the node's own operator (lock amendment 7). The two named cells
    /// change from master (T-22 families): <c>Int32? &lt;&lt; Int64</c> was <c>Int64?</c>, <c>UInt64? | Int32</c> was <c>UInt64?</c>.
    /// </summary>
    [Test]
    [Arguments(OperatorKind.ShiftLeft, typeof(int?), typeof(int), typeof(int?))]
    [Arguments(OperatorKind.ShiftRight, typeof(long?), typeof(int), typeof(long?))]
    [Arguments(OperatorKind.BitwiseOr, typeof(long), typeof(int?), typeof(long?))]
    [Arguments(OperatorKind.BitwiseAnd, typeof(int?), typeof(int?), typeof(int?))]
    [Arguments(OperatorKind.ShiftLeft, typeof(int?), typeof(long), null)]
    [Arguments(OperatorKind.BitwiseOr, typeof(ulong?), typeof(int), null)]
    public async Task BitwiseAndShift_WithANullableOperand_AreJudgedAsTheirOwnOperator(OperatorKind kind, Type left, Type right, Type? expected) =>
        await Assert.That(Stamp(kind, Param("a", left), Param("b", right))).IsEqualTo(expected);

    /// <summary>
    /// Lock amendment 6: the fixed <c>bool</c> arm of a comparison node equals the provider's result wherever the provider
    /// answers, so a provider answer that stops being <c>bool</c> fails here. The provider gives no answer for enum, null
    /// and reference comparison (T-25 and unowned), which is why the arm stays fixed.
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
}