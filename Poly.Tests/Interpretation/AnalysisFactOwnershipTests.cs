using Poly.Analysis;
using Poly.Interpretation;
using Poly.Interpretation.Analysis.ConstantFolding;
using Poly.Interpretation.Analysis.ControlFlow;
using Poly.Interpretation.Analysis.Semantics;

namespace Poly.Tests.Interpretation;

/// <summary>One author per analysis fact: the elision reasons, the purity predicate, the
/// binding lookup, and the constant-boolean query.</summary>
public class AnalysisFactOwnershipTests {
    private static AnalysisResult Analyze(Node ast) =>
        new AnalyzerBuilder()
            .UseThisReferenceContext()
            .UseVariableScopeValidator()
            .UseTypeAndMemberResolver()
            .UseSideEffectAnalysis()
            .UseJumpTargetResolution()
            .UseConstantFolding()
            .UseControlFlowAnalysis()
            .Build()
            .Analyze(ast);

    /// <summary>Characterisation, green before and after the de-duplication: the control
    /// flow pass decides "is this condition constant?" through exactly <c>!HasSideEffects</c>.
    /// The table is the lock that let the duplicate private predicate be deleted.</summary>
    [Test]
    public async Task IsPureAndHasSideEffects_AreExactComplements() {
        var cases = new (Node Condition, bool HasEffects, bool ElidesThen, bool ElidesElse)[] {
            (new Constant(true), false, false, true),
            (new Constant(false), false, true, false),
            (new Add(new Constant(1L), new Constant(2L)), false, false, false),
            (new Invoke(new Variable("f")), true, false, false),
            (new IndexAccess(new Variable("a"), new Constant(0L)), true, false, false),
        };

        foreach (var (condition, hasEffects, elidesThen, elidesElse) in cases) {
            var thenBranch = new Block(new Constant(1L));
            var elseBranch = new Block(new Constant(2L));
            var result = Analyze(new IfStatement(condition, thenBranch, elseBranch));

            await Assert.That(result.HasSideEffects(condition)).IsEqualTo(hasEffects);
            await Assert.That(result.CanElide(thenBranch)).IsEqualTo(elidesThen);
            await Assert.That(result.CanElide(elseBranch)).IsEqualTo(elidesElse);
        }

        // Missing side-effect metadata is not purity: a node no pass visited has effects.
        // A null node is not a node at all, so it is effect-free.
        await Assert.That(Analyze(new Constant(1L)).HasSideEffects(new Constant(5L))).IsTrue();
        await Assert.That(Analyze(new Constant(1L)).HasSideEffects(null)).IsFalse();
    }

    /// <summary>The single elision writer records every reason that applies, and
    /// unreachable alone is sufficient: the precedence rule lives next to the writer.</summary>
    [Test]
    public async Task BothElisionReasons_ArePreserved_AndPrecedenceIsRecorded() {
        var unreachablePure = new Constant(1L);
        var pureLoop = new WhileLoop(new Constant(false), new Block(unreachablePure, new Constant(2L)));
        var result = Analyze(new Block(pureLoop, new Constant(3L)));

        var combined = result.GetMetadata<ElisionMetadata>(unreachablePure);
        await Assert.That(combined).IsNotNull();
        await Assert.That(combined!.Reasons)
            .IsEqualTo(ElisionReason.Unreachable | ElisionReason.NoObservableEffect);
        await Assert.That(combined.CanElide).IsTrue();

        var effectful = new Invoke(new Variable("f"));
        var effectfulResult = Analyze(new WhileLoop(new Constant(false), new Block(effectful)));
        var unreachableOnly = effectfulResult.GetMetadata<ElisionMetadata>(effectful);
        await Assert.That(unreachableOnly).IsNotNull();
        await Assert.That(unreachableOnly!.Reasons).IsEqualTo(ElisionReason.Unreachable);
        await Assert.That(effectfulResult.CanElide(effectful)).IsTrue();

        // A live final value is used, so the side-effect reason does not reach it.
        var final = new Constant(9L);
        var liveResult = Analyze(new Block(new Constant(1L), final));
        await Assert.That(liveResult.GetMetadata<ElisionMetadata>(final)).IsNull();
        await Assert.That(liveResult.CanElide(final)).IsFalse();
    }

    /// <summary>One derivation answers "what does this use bind to".</summary>
    [Test]
    public async Task BindingLookup_HasOneAnswerPerUse() {
        var parameter = new Parameter("p");
        var local = new Variable("x");
        var localUse = new Variable("x");
        var parameterUse = new Variable("p");
        var body = new Block([new Assignment(local, new Constant(1L)), localUse, parameterUse], [local]);
        var result = Analyze(new Lambda([parameter], body));

        await Assert.That(StoredLambdaBinding.BindingOf(result, localUse)).IsSameReferenceAs(local);
        await Assert.That(StoredLambdaBinding.BindingOf(result, parameterUse)).IsSameReferenceAs(parameter);
        await Assert.That(StoredLambdaBinding.BindingOf(result, local)).IsSameReferenceAs(local);

        var binding = StoredLambdaBinding.BindingOf(result, localUse);
        await Assert.That(StoredLambdaBinding.BindingOf(result, binding)).IsSameReferenceAs(binding);
    }

    /// <summary>One constant-boolean query: <c>null</c> is "not known", never <c>false</c>.</summary>
    [Test]
    public async Task ConstBool_UnknownIsNull_NotFalse() {
        var knownTrue = new Constant(true);
        var knownFalse = new Constant(false);
        var unknown = new Add(new Constant(1L), new Constant(2L));
        var result = Analyze(new Block(
            new IfStatement(knownTrue, new Block(new Constant(1L))),
            new IfStatement(knownFalse, new Block(new Constant(1L))),
            unknown));

        await Assert.That(ControlFlowAnalysisPass.GetConstBool(result, knownTrue)).IsTrue();
        await Assert.That(ControlFlowAnalysisPass.GetConstBool(result, knownFalse)).IsFalse();
        await Assert.That(ControlFlowAnalysisPass.GetConstBool(result, unknown)).IsNull();
        await Assert.That(ControlFlowAnalysisPass.GetConstBool(result, unknown) is false).IsFalse();
        await Assert.That(ControlFlowAnalysisPass.GetConstBool(result, null)).IsNull();
    }
}