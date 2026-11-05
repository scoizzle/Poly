using Poly.Interpretation;
using Poly.Interpretation.Analysis.ConstantFolding;
using Poly.Interpretation.Analysis.ControlFlow;
using Poly.Interpretation.Analysis.Semantics;

namespace Poly.Tests.Interpretation;

/// <summary>
/// Throwing is a node effect, not a second table: <see cref="NodeEffect"/> carries the state half and
/// the throw half as independent flags, and <see cref="SideEffectAnalyzer"/> composes them post-order.
/// The composition is where the old structural walk was wrong: a catch-all clause absorbs the try
/// block's throw, so <c>try { 1/0 } catch { }</c> is free even though the division is not.
/// </summary>
public class EffectCompositionTests {
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
    public async Task Divide_ThrowsWithoutAStateEffect() {
        var divide = new Divide(Wrap(1), Wrap(0));

        await Assert.That(NodeFacts.Effects(divide)).IsEqualTo(NodeEffect.Throws)
            .Because("integer division changes nothing and can raise DivideByZeroException");
        await Assert.That(NodeFacts.Throws(divide)).IsTrue();
    }

    [Test]
    public async Task Constant_IsFree() {
        await Assert.That(NodeFacts.Effects(Wrap(1L))).IsEqualTo(NodeEffect.None);
    }

    [Test]
    public async Task Member_ReadsAndThrows() {
        await Assert.That(NodeFacts.Effects(new Member(new Variable("o"), "Length")))
            .IsEqualTo(NodeEffect.Read | NodeEffect.Throws)
            .Because("a property read observes state and can throw on a null receiver");
    }

    [Test]
    public async Task NewArray_AllocatesAndThrows() {
        await Assert.That(NodeFacts.Effects(new NewArray(TypeReference.To<int>(), Wrap(1))))
            .IsEqualTo(NodeEffect.Allocate | NodeEffect.Throws)
            .Because("array creation allocates and raises OverflowException on a negative length");
    }

    [Test]
    public async Task ThrowingExpression_HasNoStateEffect() {
        var program = new Block([new Assignment(new Variable("x"), new Divide(Wrap(1), Wrap(0))), Wrap(1)], [new Variable("x")]);
        var divide = ((Assignment)program.Nodes[0]).Value;
        var analysis = Analyze(program);

        await Assert.That(analysis.HasSideEffects(divide)).IsFalse()
            .Because("a throw is not a state change: a condition containing a division can still be pruned");
        await Assert.That(analysis.GetMetadata<SideEffectMetadata>(divide)!.Throws).IsTrue();
    }

    [Test]
    public async Task CatchAll_AbsorbsTheTryBlocksThrow() {
        var tryCatch = new TryCatchFinally(
            new Divide(Wrap(1), Wrap(0)),
            [new CatchClause(null, null, Wrap(1))]);
        var program = new Block([tryCatch, Wrap(1)]);
        var analysis = Analyze(program);

        await Assert.That(analysis.GetMetadata<SideEffectMetadata>(tryCatch)!.Throws).IsFalse()
            .Because("the catch-all clause takes the exception, so nothing leaves the statement");
        await Assert.That(analysis.CanElide(tryCatch)).IsTrue();
    }

    [Test]
    public async Task TypedCatch_DoesNotAbsorbTheThrow() {
        var tryCatch = new TryCatchFinally(
            new Divide(Wrap(1), Wrap(0)),
            [new CatchClause(TypeReference.To<InvalidOperationException>(), null, Wrap(1))]);
        var program = new Block([tryCatch, Wrap(1)]);
        var analysis = Analyze(program);

        await Assert.That(analysis.GetMetadata<SideEffectMetadata>(tryCatch)!.Throws).IsTrue()
            .Because("InvalidOperationException does not catch DivideByZeroException, and the analysis has no exception-type lattice");
        await Assert.That(analysis.CanElide(tryCatch)).IsFalse();
    }

    [Test]
    public async Task NestedCatchAll_MakesTheOuterCatchUnreachable() {
        var outerCatchBody = new Block(new Constant(1L));
        var inner = new TryCatchFinally(
            new Divide(Wrap(1), Wrap(0)),
            [new CatchClause(null, null, Wrap(1))]);
        var outer = new TryCatchFinally(
            inner,
            [new CatchClause(TypeReference.To<Exception>(), null, outerCatchBody)]);
        var analysis = Analyze(outer);

        await Assert.That(analysis.CanElide(outerCatchBody)).IsTrue()
            .Because("the inner catch-all takes the exception, so the outer try cannot throw");
        await Assert.That(analysis.Diagnostics.Any(d => d.Code == "CF0010")).IsTrue();
    }

    [Test]
    public async Task Lambda_KeepsStateEffectsButNotItsThrow() {
        var p = new Parameter("p", TypeReference.To<int>());
        var lambda = new Lambda([p], new Divide(p, Wrap(2)));
        var analysis = Analyze(new Block([lambda, Wrap(1)]));

        await Assert.That(analysis.GetMetadata<SideEffectMetadata>(lambda)!.Throws).IsFalse()
            .Because("the body throws when something calls it, not when the delegate is created");
    }
}