using Poly.DomainModeling.Lowering;
using Poly.Syntax.Analysis;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Analysis pass that produces <see cref="BehaviorMetadata"/> —
/// per-entity action metadata (parameters, return types, effective policies, stage transitions).
///
/// Wraps <see cref="BehaviorAnalyzer"/> as a pass. No dependencies on other infra passes.
/// </summary>
internal sealed class BehaviorPass : INodeAnalyzer {
    public const string Id = "BehaviorPass";
    public string PassName => Id;
    public string[] Dependencies => [OwnershipAggregatePass.Id];
    // Semantic/Capability metadata is consumed from priorAnalysis/AnalysisContext.

    private readonly AnalysisResult? _analysis;

    public BehaviorPass(AnalysisResult? analysis = null) {
        _analysis = analysis;
    }

    public void Analyze(AnalysisContext context, Node node) {
        if (node is not Domain domain) return;
        if (context.HasStructuralFailure) return;

        var analyzer = new BehaviorAnalyzer(domain, _analysis, context);
        var behavior = analyzer.Analyze();
        context.SetMetadata(domain, new BehaviorMetadata(behavior));
    }
}