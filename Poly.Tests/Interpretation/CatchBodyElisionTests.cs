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
            new Invoke(new Member(new ClrTypeReference(typeof(string)), "Concat"), Wrap("a"), Wrap("b"))));

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
}