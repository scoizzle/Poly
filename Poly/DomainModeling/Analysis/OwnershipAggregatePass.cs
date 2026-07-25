using Poly.DomainModeling.Lowering;
using Poly.Syntax.Analysis;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Analysis pass that produces <see cref="OwnershipAggregateMetadata"/> —
/// the cross-entity ownership hierarchy.
///
/// Wraps <see cref="AggregateAnalyzer"/> as a pass, consuming
/// <see cref="EffectTopologyMetadata"/> for create-in parent prioritization
/// and <see cref="EntityStructureMetadata"/> for root detection.
/// Depends on <see cref="EffectTopologyPass"/> and <see cref="EntityStructureAnalyzer"/>.
/// </summary>
internal sealed class OwnershipAggregatePass : INodeAnalyzer {
    public const string Id = "OwnershipAggregatePass";
    public string PassName => Id;
    public string[] Dependencies => [EffectTopologyPass.Id, EntityStructureAnalyzer.Id];

    public void Analyze(AnalysisContext context, Node node) {
        if (node is not Domain domain) return;
        if (context.HasStructuralFailure) return;

        var topology = context.GetMetadata<EffectTopologyMetadata>(domain)?.Topology;
        var analyzer = new AggregateAnalyzer(domain, context: context);
        var aggregate = analyzer.Analyze(topology);
        context.SetMetadata(domain, new OwnershipAggregateMetadata(aggregate));

        // ── Diagnostics (B1) ──────────────────────────────────
        var entities = domain.Types.OfType<Entity>().ToList();

        // DMAGG001: non-root with no aggregate parent — orphan warning
        foreach (var e in entities) {
            var agg = aggregate.Entities.FirstOrDefault(a => a.Name == e.Name);
            if (agg is null) continue;
            if (!agg.IsRoot && agg.AggregateParentName is null) {
                context.ReportWarning(e,
                    $"Entity '{e.Name}' is a non-root entity with no aggregate parent. " +
                    "It may be orphaned — verify the relationship hierarchy or add a parent relationship.",
                    DomainModelDiagnosticCodes.AggregateOrphan);
            }
        }
    }
}