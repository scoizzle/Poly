using Poly.Interpretation;
using Poly.Interpretation.Analysis.ConstantFolding;
using Poly.Interpretation.Analysis.ControlFlow;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.CSharp;

namespace Poly.Tests.Interpretation;

/// <summary>
/// A non-final statement whose result is unused is elided only when executing it cannot raise.
/// <see cref="SideEffectAnalyzer"/> used to decide that from the side-effect kind alone, and a bare
/// indexed read is classified <c>Read</c> — so the writer deleted it and the printed program could
/// no longer throw where the VM does. The guard is the raising column, walked through inert wrappers,
/// because <c>Or</c>, <c>Conditional</c>, <c>Coalesce</c> and a nested <c>Block</c> are inert at the
/// root and can hold the division or the call.
/// </summary>
public class SideEffectElisionTests {
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

    [Test]
    public async Task BareIndexedRead_NonFinal_IsNotElidable() {
        var a = new Variable("a");
        var i = new Variable("i");
        var read = new IndexAccess(a, i);
        var program = new Block([
            new Assignment(a, new NewArray(TypeReference.To<int>(), Wrap(1))),
            new Assignment(i, Wrap(2)),
            read,
            Wrap(1),
        ], [a, i]);

        var analysis = Analyze(program);
        var printed = new CSharpGenerator(analysis).Generate(program);

        await Assert.That(analysis.CanElide(read)).IsFalse()
            .Because("an indexed read can throw, so deleting it changes what the program does");
        await Assert.That(printed).Contains("a[i]");
    }

    [Test]
    public async Task RaisingChildUnderAnInertWrapper_IsNotElidable() {
        var x = new Variable("x");
        var y = new Variable("y");
        var root = new Or(Wrap(true), new GreaterThan(new Divide(x, y), Wrap(0)));
        var program = new Block([
            new Assignment(x, Wrap(1)),
            new Assignment(y, Wrap(0)),
            root,
            Wrap(1),
        ], [x, y]);

        var analysis = Analyze(program);

        await Assert.That(analysis.CanElide(root)).IsFalse()
            .Because("Or is inert itself and short-circuits, but the division on the right still raises");
    }

    [Test]
    public async Task NestedBlockWithARaisingChild_IsNotElidable() {
        var a = new Variable("a");
        var i = new Variable("i");
        var nested = new Block(new IndexAccess(a, i), Wrap(1));
        var program = new Block([
            new Assignment(a, new NewArray(TypeReference.To<int>(), Wrap(1))),
            new Assignment(i, Wrap(2)),
            nested,
            Wrap(1),
        ], [a, i]);

        var analysis = Analyze(program);

        await Assert.That(analysis.CanElide(nested)).IsFalse()
            .Because("a nested block is inert at the root and executes its children");
    }

    [Test]
    public async Task DecimalArithmetic_NonFinal_IsNotElidable() {
        var d = new Variable("d");
        var sum = new Add(new Constant(1m), new Constant(2m));
        var program = new Block([new Assignment(d, Wrap(0m)), sum, Wrap(1)], [d]);

        var analysis = Analyze(program);

        await Assert.That(analysis.CanElide(sum)).IsFalse()
            .Because("Add is marked raising because the same kind reaches the decimal operator, which throws; this pair cannot overflow, so the test pins the over-approximation");
    }

    [Test]
    public async Task BareConstant_NonFinal_IsStillElidable() {
        var pure = new Constant(1L);
        var program = new Block([pure, Wrap(2L)]);

        var analysis = Analyze(program);

        await Assert.That(analysis.CanElide(pure)).IsTrue()
            .Because("a constant cannot raise, so the optimization still applies");
    }

    [Test]
    public async Task ForInitializerThatCanRaise_IsNotElidable() {
        var a = new Variable("a");
        var i = new Variable("i");
        var init = new IndexAccess(a, i);
        var loop = new ForLoop(init, Wrap(false), null, new Block(Wrap(1)));
        var program = new Block([new Assignment(a, new NewArray(TypeReference.To<int>(), Wrap(1))), loop, Wrap(1)], [a, i]);

        var analysis = Analyze(program);

        await Assert.That(analysis.CanElide(init)).IsFalse()
            .Because("a for initializer that can raise must not be elided either");
    }

    [Test]
    public async Task LambdaStatementWithARaisingBody_IsStillElidable() {
        var p = new Parameter("p", TypeReference.To<int>());
        var lambda = new Lambda([p], new Divide(p, Wrap(2)));
        var program = new Block([lambda, Wrap(1)]);

        var analysis = Analyze(program);
        var printed = new CSharpGenerator(analysis).Generate(program);

        await Assert.That(analysis.CanElide(lambda)).IsTrue()
            .Because("creating a delegate does not run its body, and the writer cannot print a lambda statement");
        await Assert.That(printed).DoesNotContain("=>");
    }

    [Test]
    public async Task ForIncrementThatCanRaise_IsNotElidable() {
        var a = new Variable("a");
        var i = new Variable("i");
        var increment = new IndexAccess(a, i);
        var loop = new ForLoop(null, Wrap(false), increment, new Block(Wrap(1)));
        var program = new Block([new Assignment(a, new NewArray(TypeReference.To<int>(), Wrap(1))), loop, Wrap(1)], [a, i]);

        var analysis = Analyze(program);

        await Assert.That(analysis.CanElide(increment)).IsFalse()
            .Because("a for increment that can raise must not be elided either");
    }

    [Test]
    public async Task RaisingStatement_GetsNoElisionDiagnostic() {
        var a = new Variable("a");
        var i = new Variable("i");
        var read = new IndexAccess(a, i);
        var program = new Block([
            new Assignment(a, new NewArray(TypeReference.To<int>(), Wrap(1))),
            new Assignment(i, Wrap(2)),
            read,
            Wrap(1),
        ], [a, i]);
        var settings = AnalysisSettings.Default.With(new SideEffectAnalysisOptions { EmitElisionDiagnostics = true });
        var analysis = new AnalyzerBuilder()
            .UseThisReferenceContext().UseVariableScopeValidator().UseTypeAndMemberResolver()
            .UseSideEffectAnalysis().UseJumpTargetResolution().UseConstantFolding()
            .UseControlFlowAnalysis().Build().Analyze(program, settings: settings);

        await Assert.That(analysis.Diagnostics.Any(d => d.Code == "DEAD_CODE_ELIDABLE" && ReferenceEquals(d.Node, read))).IsFalse()
            .Because("the diagnostic must not claim a statement the writer keeps can be elided");
    }
}