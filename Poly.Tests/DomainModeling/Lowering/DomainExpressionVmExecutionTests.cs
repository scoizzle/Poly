using Poly.DomainModeling;
using Poly.DomainModeling.Lowering;
using Poly.Interpretation;
using Poly.Interpretation.Analysis;
using Poly.Interpretation.Analysis.ConstantFolding;
using Poly.Interpretation.Analysis.ControlFlow;
using Poly.Interpretation.Analysis.LoweringPrep;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.LinqExpressions;
using Poly.Interpretation.Vm;
using Poly.Syntax;
using Poly.Syntax.Analysis;

using SN = Poly.Syntax.Nodes;

namespace Poly.Tests.DomainModeling.Lowering;

public record PersonRecord(string Name, int Age);

/// <summary>
/// End-to-end tests: DomainExpression → lowering pass → Syntax AST
/// → VM analysis pipeline → µops → Vm.Execute.
/// </summary>
public class DomainExpressionVmExecutionTests {
    private static readonly DomainExpressionLoweringPass Pass = new();
    private static readonly SN.ParameterReference Subject = new();

    private static AnalysisResult Analyze(Node node) {
        return new AnalyzerBuilder()
            .UseTypeAndMemberResolver()
            .UseConstantFolding()
            .UseSideEffectAnalysis()
            .UseThisReferenceContext()
            .UseControlFlowAnalysis()
            .UseVariableScopeValidator()
            .UseDefiniteAssignmentAnalysis()
            .UseLoweringPreparation()
            .UseUopGeneration()
            .Build()
            .Analyze(node);
    }

    private static InterpreterResult Execute(Node node) {
        var (result, _) = ExecuteWithState(node);
        return result;
    }

    private static (InterpreterResult Result, VmState State) ExecuteWithState(Node node) {
        var analysis = Analyze(node);
        var lowerResult = Poly.Interpretation.Vm.Lowering.Lower(node, analysis);
        var program = ProgramCompiler.Compile(lowerResult, mode: CompilationMode.Normal);
        var state = new VmState(program) { MaxLoopIterations = 100_000_000 };
        var result = Vm.Execute(state);
        return (result, state);
    }

    private InterpreterResult ExecuteDomain(DomainExpression expr) {
        var node = Pass.Lower(expr, Subject);
        return Execute(node);
    }

    private InterpreterResult ExecuteDomain(DomainExpression expr, Node subject) {
        var node = Pass.Lower(expr, subject);
        return Execute(node);
    }

    [Test]
    public async Task Literal_42_Returns42() {
        var result = ExecuteDomain(DomainExpression.Literal(42));

        await Assert.That(result.HasValue).IsTrue();
        await Assert.That((long)result.Value!).IsEqualTo(42L);
    }

    [Test]
    public async Task Literal_Negative_ReturnsNegative() {
        var result = ExecuteDomain(DomainExpression.Literal(-7));

        await Assert.That(result.HasValue).IsTrue();
        await Assert.That((long)result.Value!).IsEqualTo(-7L);
    }

    [Test]
    public async Task Add_1Plus2_Returns3() {
        var result = ExecuteDomain(
            DomainExpression.Add(DomainExpression.Literal(1), DomainExpression.Literal(2)));

        await Assert.That((long)result.Value!).IsEqualTo(3L);
    }

    [Test]
    public async Task Subtract_10Minus3_Returns7() {
        var result = ExecuteDomain(
            DomainExpression.Subtract(DomainExpression.Literal(10), DomainExpression.Literal(3)));

        await Assert.That((long)result.Value!).IsEqualTo(7L);
    }

    [Test]
    public async Task Multiply_4Times5_Returns20() {
        var result = ExecuteDomain(
            DomainExpression.Multiply(DomainExpression.Literal(4), DomainExpression.Literal(5)));

        await Assert.That((long)result.Value!).IsEqualTo(20L);
    }

    [Test]
    public async Task Divide_10DividedBy2_Returns5() {
        var result = ExecuteDomain(
            DomainExpression.Divide(DomainExpression.Literal(10), DomainExpression.Literal(2)));

        await Assert.That((long)result.Value!).IsEqualTo(5L);
    }

    [Test]
    public async Task CompoundArithmetic_MultiplyBeforeAdd() {
        var result = ExecuteDomain(
            DomainExpression.Add(
                DomainExpression.Multiply(DomainExpression.Literal(2), DomainExpression.Literal(3)),
                DomainExpression.Literal(1)));

        await Assert.That((long)result.Value!).IsEqualTo(7L);
    }

    [Test]
    public async Task Equal_True() {
        var result = ExecuteDomain(
            DomainExpression.Equal(DomainExpression.Literal(5), DomainExpression.Literal(5)));

        await Assert.That((long)result.Value!).IsEqualTo(1L);
    }

    [Test]
    public async Task Equal_False() {
        var result = ExecuteDomain(
            DomainExpression.Equal(DomainExpression.Literal(5), DomainExpression.Literal(3)));

        await Assert.That((long)result.Value!).IsEqualTo(0L);
    }

    [Test]
    public async Task NotEqual_True() {
        var result = ExecuteDomain(
            DomainExpression.NotEqual(DomainExpression.Literal(5), DomainExpression.Literal(3)));

        await Assert.That((long)result.Value!).IsEqualTo(1L);
    }

    [Test]
    public async Task LessThan_True() {
        var result = ExecuteDomain(
            DomainExpression.LessThan(DomainExpression.Literal(3), DomainExpression.Literal(5)));

        await Assert.That((long)result.Value!).IsEqualTo(1L);
    }

    [Test]
    public async Task LessThan_False() {
        var result = ExecuteDomain(
            DomainExpression.LessThan(DomainExpression.Literal(5), DomainExpression.Literal(3)));

        await Assert.That((long)result.Value!).IsEqualTo(0L);
    }

    [Test]
    public async Task GreaterThan_True() {
        var result = ExecuteDomain(
            DomainExpression.GreaterThan(DomainExpression.Literal(5), DomainExpression.Literal(3)));

        await Assert.That((long)result.Value!).IsEqualTo(1L);
    }

    [Test]
    public async Task GreaterThanOrEqual_Equal_True() {
        var result = ExecuteDomain(
            DomainExpression.GreaterThanOrEqual(DomainExpression.Literal(5), DomainExpression.Literal(5)));

        await Assert.That((long)result.Value!).IsEqualTo(1L);
    }

    [Test]
    public async Task GreaterThanOrEqual_Greater_True() {
        var result = ExecuteDomain(
            DomainExpression.GreaterThanOrEqual(DomainExpression.Literal(7), DomainExpression.Literal(5)));

        await Assert.That((long)result.Value!).IsEqualTo(1L);
    }

    [Test]
    public async Task LessThanOrEqual_Equal_True() {
        var result = ExecuteDomain(
            DomainExpression.LessThanOrEqual(DomainExpression.Literal(3), DomainExpression.Literal(3)));

        await Assert.That((long)result.Value!).IsEqualTo(1L);
    }

    [Test]
    public async Task LessThanOrEqual_Less_True() {
        var result = ExecuteDomain(
            DomainExpression.LessThanOrEqual(DomainExpression.Literal(3), DomainExpression.Literal(5)));

        await Assert.That((long)result.Value!).IsEqualTo(1L);
    }

    [Test]
    public async Task And_BothTrue_ReturnsTrue() {
        var result = ExecuteDomain(
            DomainExpression.And(
                DomainExpression.Equal(DomainExpression.Literal(1), DomainExpression.Literal(1)),
                DomainExpression.Equal(DomainExpression.Literal(2), DomainExpression.Literal(2))));

        await Assert.That((long)result.Value!).IsEqualTo(1L);
    }

    [Test]
    public async Task And_OneFalse_ReturnsFalse() {
        var result = ExecuteDomain(
            DomainExpression.And(
                DomainExpression.Equal(DomainExpression.Literal(1), DomainExpression.Literal(1)),
                DomainExpression.Equal(DomainExpression.Literal(2), DomainExpression.Literal(3))));

        await Assert.That((long)result.Value!).IsEqualTo(0L);
    }

    [Test]
    public async Task Or_OneTrue_ReturnsTrue() {
        var result = ExecuteDomain(
            DomainExpression.Or(
                DomainExpression.Equal(DomainExpression.Literal(1), DomainExpression.Literal(2)),
                DomainExpression.Equal(DomainExpression.Literal(2), DomainExpression.Literal(2))));

        await Assert.That((long)result.Value!).IsEqualTo(1L);
    }

    [Test]
    public async Task Not_True_ReturnsFalse() {
        var result = ExecuteDomain(
            DomainExpression.Not(DomainExpression.Equal(DomainExpression.Literal(1), DomainExpression.Literal(1))));

        await Assert.That((long)result.Value!).IsEqualTo(0L);
    }

    [Test]
    public async Task Not_False_ReturnsTrue() {
        var result = ExecuteDomain(
            DomainExpression.Not(DomainExpression.Equal(DomainExpression.Literal(1), DomainExpression.Literal(2))));

        await Assert.That((long)result.Value!).IsEqualTo(1L);
    }

    // ── PropertyAccess via LINQ path ──────────────────────────────────────
    // The lowering pass correctly maps PropertyAccess → Member(subject, name).
    // The LINQ expression generator compiles Member for any CLR type natively.
    // The VM µop path for heap-allocated objects is a gap (see next section).

    private static InterpreterResult ExecuteViaLinq(Node node) {
        var analysis = new AnalyzerBuilder()
            .UseTypeAndMemberResolver()
            .UseConstantFolding()
            .UseSideEffectAnalysis()
            .UseThisReferenceContext()
            .UseControlFlowAnalysis()
            .UseVariableScopeValidator()
            .UseDefiniteAssignmentAnalysis()
            .Build()
            .Analyze(node);
        var generator = new LinqExpressionGenerator(analysis);
        var compiled = generator.Compile(node);
        var lambda = System.Linq.Expressions.Expression
            .Lambda<Func<object?>>(System.Linq.Expressions.Expression.Convert(compiled.Expression, typeof(object)))
            .Compile();
        var value = lambda();
        return InterpreterResult.FromValue(value);
    }

    private InterpreterResult ExecuteDomainViaLinq(DomainExpression expr, Node subject) {
        var node = Pass.Lower(expr, subject);
        return ExecuteViaLinq(node);
    }

    [Test]
    public async Task PropertyAccess_OnClrRecord_ReturnsPropertyValue() {
        var person = new PersonRecord("Alice", 30);
        var subject = new SN.Constant(person);

        var node = Pass.Lower(DomainExpression.Property("Name"), subject);
        var (result, state) = ExecuteWithState(node);

        // Name returns a string stored on the VM heap. The result is a heap handle (int).
        // Dereference it to get the actual string.
        await Assert.That(result.HasValue).IsTrue();
        var handle = (int)(long)result.Value!;
        await Assert.That(state.Heap.Get(handle)).IsEqualTo("Alice");
        state.Dispose();
    }

    [Test]
    public async Task PropertyAccess_OnClrRecord_ReturnsNumericProperty() {
        var person = new PersonRecord("Bob", 42);
        var subject = new SN.Constant(person);

        var result = ExecuteDomain(DomainExpression.Property("Age"), subject);

        await Assert.That(result.HasValue).IsTrue();
        await Assert.That(result.Value).IsEqualTo((long)42);
    }

    [Test]
    public async Task PropertyAccess_WithArithmetic_ComputesCorrectly() {
        var person = new PersonRecord("Charlie", 25);
        var subject = new SN.Constant(person);

        var result = ExecuteDomain(
            DomainExpression.Add(DomainExpression.Property("Age"), DomainExpression.Literal(10)),
            subject);

        await Assert.That(result.HasValue).IsTrue();
        await Assert.That(result.Value).IsEqualTo((long)35);
    }

    [Test]
    public async Task PropertyAccess_WithComparison_ComputesCorrectly() {
        var person = new PersonRecord("Diana", 17);
        var subject = new SN.Constant(person);

        var result = ExecuteDomain(
            DomainExpression.GreaterThan(DomainExpression.Property("Age"), DomainExpression.Literal(18)),
            subject);

        await Assert.That((long)result.Value!).IsEqualTo(0L);
    }

    [Test]
    public async Task DeeplyNestedExpression() {
        // ((2 + 3) * 4) > 15  →  (5 * 4) > 15  →  20 > 15  →  true (1)
        var result = ExecuteDomain(
            DomainExpression.GreaterThan(
                DomainExpression.Multiply(
                    DomainExpression.Add(DomainExpression.Literal(2), DomainExpression.Literal(3)),
                    DomainExpression.Literal(4)),
                DomainExpression.Literal(15)));

        await Assert.That((long)result.Value!).IsEqualTo(1L);
    }

}