using Poly.Analysis;
using Poly.Interpretation;
using Poly.Interpretation.Analysis.Semantics;

namespace Poly.Tests.Interpretation;

/// <summary>Side-effect kind / elision metadata and optional DEAD_CODE_ELIDABLE diagnostics.</summary>
public class SideEffectAnalysisTests {
    private static Analyzer SideEffectAnalyzerPipeline() =>
        new AnalyzerBuilder()
            .UseThisReferenceContext()
            .UseTypeAndMemberResolver()
            .UseVariableScopeValidator()
            .UseSideEffectAnalysis()
            .Build();

    [Test]
    public async Task Constant_IsPure() {
        var c = new Constant(42L);
        var result = SideEffectAnalyzerPipeline().Analyze(c);
        var meta = result.GetMetadata<SideEffectMetadata>(c);
        await Assert.That(meta).IsNotNull();
        await Assert.That(meta!.Kind).IsEqualTo(SideEffectKind.Pure);
        await Assert.That(result.HasSideEffects(c)).IsFalse();
    }

    [Test]
    public async Task Assignment_IsWrite() {
        var x = new Variable("x");
        var assign = new Assignment(x, new Constant(1L));
        var block = new Block([assign], [x]);
        var result = SideEffectAnalyzerPipeline().Analyze(block);
        var meta = result.GetMetadata<SideEffectMetadata>(assign);
        await Assert.That(meta).IsNotNull();
        await Assert.That(meta!.Kind).IsEqualTo(SideEffectKind.Write);
    }

    [Test]
    public async Task PureNonFinalInBlock_IsElidable() {
        var pure = new Constant(1L);
        var block = new Block([pure, new Constant(2L)]);
        var result = SideEffectAnalyzerPipeline().Analyze(block);
        await Assert.That(result.CanElide(pure)).IsTrue();
        await Assert.That(result.CanElide(block.Nodes[^1])).IsFalse();
    }

    [Test]
    public async Task EmitElisionDiagnostics_ReportsDeadCodeElidable() {
        var pure = new Add(new Constant(1L), new Constant(2L));
        var block = new Block([pure, new Constant(9L)]);
        var settings = AnalysisSettings.Default.With(
            new SideEffectAnalysisOptions { EmitElisionDiagnostics = true });
        var result = SideEffectAnalyzerPipeline().Analyze(block, settings: settings);
        await Assert.That(result.Diagnostics.Any(d => d.Code == "DEAD_CODE_ELIDABLE")).IsTrue();
    }

    [Test]
    public async Task Assignment_NonFinal_ValueNotUsed() {
        var x = new Variable("x");
        var assign = new Assignment(x, new Constant(1L));
        var block = new Block([assign, x], [x]);
        var result = SideEffectAnalyzerPipeline().Analyze(block);
        var used = result.GetMetadata<AssignmentValueUsedMetadata>(assign);
        await Assert.That(used).IsNotNull();
        await Assert.That(used!.IsValueUsed).IsFalse();
    }

    [Test]
    public async Task Assignment_Final_ValueUsed() {
        var x = new Variable("x");
        var assign = new Assignment(x, new Constant(1L));
        var block = new Block([assign], [x]);
        var result = SideEffectAnalyzerPipeline().Analyze(block);
        var used = result.GetMetadata<AssignmentValueUsedMetadata>(assign);
        await Assert.That(used).IsNotNull();
        await Assert.That(used!.IsValueUsed).IsTrue();
    }

    [Test]
    public async Task PureNonFinal_HasElisionMetadata() {
        var pure = new Constant(1L);
        var block = new Block([pure, new Constant(2L)]);
        var result = SideEffectAnalyzerPipeline().Analyze(block);
        var elision = result.GetMetadata<ElisionMetadata>(pure);
        await Assert.That(elision).IsNotNull();
        await Assert.That(elision!.CanElide).IsTrue();
        await Assert.That(result.GetMetadata<SideEffectMetadata>(pure)).IsNotNull();
    }
}
