using Poly.DomainModeling.Lowering;
using Poly.Syntax.Analysis;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Analysis pass that produces <see cref="OwnershipAggregateMetadata"/> —
/// the cross-entity ownership hierarchy.
///
/// Wraps <see cref="AggregateAnalyzer"/> as a pass, consuming
/// <see cref="EffectTopologyMetadata"/> for create-in parent prioritization.
/// Depends on <see cref="EffectTopologyPass"/>.
/// </summary>
internal sealed class OwnershipAggregatePass : INodeAnalyzer {
    public const string Id = "OwnershipAggregatePass";
    public string PassName => Id;
    public string[] Dependencies => [EffectTopologyPass.Id];

    private readonly AnalysisResult? _analysis;

    public OwnershipAggregatePass(AnalysisResult? analysis = null) {
        _analysis = analysis;
    }

    public void Analyze(AnalysisContext context, Node node) {
        if (node is not Domain domain) return;
        if (context.HasStructuralFailure) return;

        var topology = context.GetMetadata<EffectTopologyMetadata>(domain)?.Topology;
        var analyzer = new AggregateAnalyzer(domain, _analysis);
        var aggregate = analyzer.Analyze(topology);
        context.SetMetadata(domain, new OwnershipAggregateMetadata(aggregate));
    }
}