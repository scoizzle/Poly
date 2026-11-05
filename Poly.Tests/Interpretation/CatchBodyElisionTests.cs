using Poly.Interpretation;
using Poly.Interpretation.Analysis.ConstantFolding;
using Poly.Interpretation.Analysis.ControlFlow;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.CSharp;

namespace Poly.Tests.Interpretation;

/// <summary>
/// A catch clause is reachable when anything in its try block can raise. The control-flow pass used
/// to answer that question with "does the try block contain a literal throw statement", so a try
/// block whose only raising operation was a division had its catch body marked unreachable — and the
/// writer, honouring that fact, dropped all but the last statement of the catch. The VM ran it. Two
/// consumers of one tree disagreed.
/// </summary>
public class CatchBodyElisionTests {
    static AnalysisResult Analyze(Node ast) => new AnalyzerBuilder()
        .UseThisReferenceContext()
        .UseVariableScopeValidator()
        .UseTypeAndMemberResolver()
        .UseSideEffectAnalysis()
        .UseJumpTargetResolution()
        .UseConstantFolding()
        .UseControlFlowAnalysis()
        .Build()
        .Analyze(ast);

    static (TryCatchFinally Program, Block CatchBody) Build(Node tryBlock) {
        var caught = new Variable("caught");
        var acc = new Variable("acc");
        var catchBody = new Block(new Assignment(caught, Wrap(1)), new Assignment(acc, Wrap(41)));
        return (new TryCatchFinally(tryBlock, [new CatchClause(TypeReference.To<DivideByZeroException>(), null, catchBody)]), catchBody);
    }

    [Test]
    public async Task Try_WithDivide_CatchBodyIsReachable() {
        var caught = new Variable("caught");
        var den = new Variable("den");
        var (program, catchBody) = Build(new Block(
            new Assignment(caught, Wrap(0)),
            new Assignment(den, Wrap(0)),
            new Assignment(caught, new Divide(Wrap(10), den))));

        var analysis = Analyze(program);

        await Assert.That(analysis.CanElide(catchBody)).IsFalse()
            .Because("a division in the try block can raise, so the catch body executes");
        await Assert.That(analysis.Diagnostics.Any(d => d.Code == "CF0010")).IsFalse();
    }

    [Test]
    public async Task Try_WithInvoke_CatchBodyIsReachable() {
        var (program, catchBody) = Build(new Block(
            new Invoke(new Lambda([], Wrap(1)))));

        var analysis = Analyze(program);

        await Assert.That(analysis.CanElide(catchBody)).IsFalse();
        await Assert.That(analysis.Diagnostics.Any(d => d.Code == "CF0010")).IsFalse();
    }

    [Test]
    public async Task Try_WithDivide_WriterPrintsEveryCatchStatement() {
        var caught = new Variable("caught");
        var den = new Variable("den");
        var acc = new Variable("acc");
        var (program, _) = Build(new Block(
            new Assignment(caught, Wrap(0)),
            new Assignment(acc, Wrap(0)),
            new Assignment(den, Wrap(0)),
            new Assignment(acc, new Divide(Wrap(10), den))));

        var analysis = Analyze(program);
        var printed = new CSharpGenerator(analysis).Generate(program);

        await Assert.That(printed).Contains("caught = 1")
            .Because("the first statement of a reachable catch body must survive printing");
        await Assert.That(printed).Contains("acc = 41");
    }

    [Test]
    public async Task Try_WithLiteralBody_StillElidesCatch() {
        var (program, catchBody) = Build(Wrap(0L));

        var analysis = Analyze(program);

        await Assert.That(analysis.CanElide(catchBody)).IsTrue()
            .Because("a literal cannot raise, so the optimization still applies");
        await Assert.That(analysis.Diagnostics.Any(d => d.Code == "CF0010")).IsTrue();
    }

    [Test]
    public async Task Try_WithThrowInsideLambda_StillElidesCatch() {
        var (program, catchBody) = Build(new Block(
            new Assignment(new Variable("f"), new Lambda([], new ThrowStatement(new Variable("ex"))))));

        var analysis = Analyze(program);

        await Assert.That(analysis.CanElide(catchBody)).IsTrue()
            .Because("a lambda body throws when it is called, not in the try that creates it");
    }

    [Test]
    public async Task Try_WithThrowExpressionOfAVariable_KeepsCatchReachable() {
        var (program, catchBody) = Build(new Block(
            new Assignment(new Variable("x"), new ThrowExpression(new Variable("ex")))));

        var analysis = Analyze(program);

        await Assert.That(analysis.CanElide(catchBody)).IsFalse()
            .Because("a throw expression raises even when its payload is a variable, not a new exception");
        await Assert.That(analysis.Diagnostics.Any(d => d.Code == "CF0010")).IsFalse();
    }

    [Test]
    public async Task Try_WithUsingStatement_KeepsCatchReachable() {
        var (program, catchBody) = Build(new Block(
            new UsingStatement(new Variable("resource"), Wrap(0L))));

        var analysis = Analyze(program);

        await Assert.That(analysis.CanElide(catchBody)).IsFalse()
            .Because("Dispose runs in a finally and can raise into the enclosing catch");
        await Assert.That(analysis.Diagnostics.Any(d => d.Code == "CF0010")).IsFalse();
    }

    [Test]
    public async Task Try_WithArrayCreation_KeepsCatchReachable() {
        var (program, catchBody) = Build(new Block(
            new NewArray(TypeReference.To<int>(), new Variable("n"))));

        var analysis = Analyze(program);

        await Assert.That(analysis.CanElide(catchBody)).IsFalse()
            .Because("a negative length raises OverflowException from the allocation");
        await Assert.That(analysis.Diagnostics.Any(d => d.Code == "CF0010")).IsFalse();
    }

    [Test]
    public async Task Try_WithDecimalArithmetic_KeepsCatchReachable() {
        var d = new Variable("d");
        var (program, catchBody) = Build(new Block(
            new Assignment(d, new Add(new Constant(1m), new Constant(2m)))));

        var analysis = Analyze(program);

        await Assert.That(analysis.CanElide(catchBody)).IsFalse()
            .Because("decimal addition throws OverflowException where the integer operator wraps");
        await Assert.That(analysis.Diagnostics.Any(d => d.Code == "CF0010")).IsFalse();
    }

    [Test]
    public async Task Try_WithStridedSetBits_KeepsCatchReachable() {
        var (program, catchBody) = Build(new Block(
            new StridedSetBits(new Variable("a"), Wrap(0), Wrap(1), Wrap(99))));

        var analysis = Analyze(program);

        await Assert.That(analysis.CanElide(catchBody)).IsFalse()
            .Because("an indexed write past the array length raises");
        await Assert.That(analysis.Diagnostics.Any(d => d.Code == "CF0010")).IsFalse();
    }

    [Test]
    public async Task Try_WithMemberOnly_KeepsCatchReachable() {
        var (program, catchBody) = Build(new Block(
            new Member(new Variable("holder"), "Length")));

        var analysis = Analyze(program);

        await Assert.That(analysis.CanElide(catchBody)).IsFalse()
            .Because("a member read on a null receiver raises");
    }

    [Test]
    public async Task Try_WithNullableDecimalArithmetic_KeepsCatchReachable() {
        var left = new Parameter("left", TypeReference.To<decimal?>());
        var right = new Parameter("right", TypeReference.To<decimal?>());
        var acc = new Variable("acc");
        var (program, catchBody) = Build(new Block(
            new Assignment(acc, new Add(left, right))));

        var analysis = Analyze(program);

        await Assert.That(analysis.CanElide(catchBody)).IsFalse()
            .Because("lifted decimal addition throws OverflowException, and a Nullable<> has no PrimitiveType to read");
        await Assert.That(analysis.Diagnostics.Any(d => d.Code == "CF0010")).IsFalse();
    }

    /// <summary>A kind with no <see cref="NodeFacts"/> row: the table cannot classify it.</summary>
    private sealed record UnclassifiedNode : Node;

    [Test]
    public async Task Try_WithAnUnclassifiedKind_KeepsCatchReachable() {
        var (program, catchBody) = Build(new Block(new UnclassifiedNode()));

        var analysis = Analyze(program);

        await Assert.That(analysis.CanElide(catchBody)).IsFalse()
            .Because("a kind the table does not know must count as raising, not as inert");
        await Assert.That(analysis.Diagnostics.Any(d => d.Code == "CF0010")).IsFalse();
    }
}