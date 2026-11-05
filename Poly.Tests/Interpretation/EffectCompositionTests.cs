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
        var target = new Variable("t");
        var body = new Block(new Assignment(target, p), new Divide(p, Wrap(2)));
        var lambda = new Lambda([p], body);
        var analysis = Analyze(new Block([lambda, Wrap(1)]));

        var effects = analysis.GetMetadata<SideEffectMetadata>(lambda)!.Effects;
        await Assert.That(effects & NodeEffect.Throws).IsEqualTo(NodeEffect.None)
            .Because("the body throws when something calls it, not when the delegate is created");
        await Assert.That(effects & NodeEffect.Write).IsEqualTo(NodeEffect.Write)
            .Because("the state half is a deliberate over-approximation: the write also happens only on call");
    }

    [Test]
    public async Task CatchBodyThatThrows_KeepsTheThrow() {
        var tryCatch = new TryCatchFinally(
            new Divide(Wrap(1), Wrap(0)),
            [new CatchClause(null, null, new ThrowStatement(new Variable("ex")))]);

        var analysis = Analyze(tryCatch);

        await Assert.That(analysis.GetMetadata<SideEffectMetadata>(tryCatch)!.Throws).IsTrue()
            .Because("the catch-all absorbs the try block, then the catch body throws");
    }

    [Test]
    public async Task FinallyThatThrows_KeepsTheThrow() {
        var tryCatch = new TryCatchFinally(
            new Divide(Wrap(1), Wrap(0)),
            [new CatchClause(null, null, Wrap(1))],
            new ThrowStatement(new Variable("ex")));

        var analysis = Analyze(tryCatch);

        await Assert.That(analysis.GetMetadata<SideEffectMetadata>(tryCatch)!.Throws).IsTrue()
            .Because("absorbs the try block, but the finally block can still throw");
    }

    [Test]
    public async Task NestedCatchAll_WithAnotherThrow_KeepsTheOuterCatchReachable() {
        var outerCatchBody = new Block(new Constant(1L));
        var inner = new TryCatchFinally(
            new Divide(Wrap(1), Wrap(0)),
            [new CatchClause(null, null, Wrap(1))]);
        var outer = new TryCatchFinally(
            new Block(inner, new Divide(Wrap(2), Wrap(0))),
            [new CatchClause(TypeReference.To<Exception>(), null, outerCatchBody)]);
        var analysis = Analyze(outer);

        await Assert.That(analysis.GetMetadata<SideEffectMetadata>(outer)!.Throws).IsTrue()
            .Because("the inner catch-all swallows one division, the second still throws");
        await Assert.That(analysis.CanElide(outerCatchBody)).IsFalse();
    }

    [Test]
    public async Task MixedTypedAndCatchAll_StillAbsorbs() {
        var tryCatch = new TryCatchFinally(
            new Divide(Wrap(1), Wrap(0)),
            [
                new CatchClause(TypeReference.To<InvalidOperationException>(), null, Wrap(1)),
                new CatchClause(null, null, Wrap(2)),
            ]);

        var analysis = Analyze(tryCatch);

        await Assert.That(analysis.GetMetadata<SideEffectMetadata>(tryCatch)!.Throws).IsFalse()
            .Because("one catch-all clause takes anything the typed clause does not");
    }

    [Test]
    public async Task ElidableTry_IsDroppedByTheWriter() {
        var tryCatch = new TryCatchFinally(
            new Divide(Wrap(1), Wrap(0)),
            [new CatchClause(null, null, Wrap(1))]);
        var program = new Block([tryCatch, Wrap(1)]);
        var analysis = Analyze(program);

        var printed = new Poly.Interpretation.CSharp.CSharpGenerator(analysis).Generate(program);

        await Assert.That(analysis.CanElide(tryCatch)).IsTrue();
        await Assert.That(printed).DoesNotContain("catch")
            .Because("the statement is free, so the writer drops it whole");
    }

    [Test]
    public async Task DeadShortCircuitBranch_StillPrunesWhenTheRightSideReads() {
        var body = new Block(new Constant(1L));
        var loop = new WhileLoop(
            new And(Wrap(false), new Member(new Variable("o"), "Flag")),
            body);

        var analysis = Analyze(loop);

        await Assert.That(analysis.CanElide(body)).IsTrue()
            .Because("false && X never evaluates X, so the member read cannot stop the prune");
    }
}