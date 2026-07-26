using Poly.Analysis;
using Poly.DomainModeling.Lowering;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Analysis pass that produces <see cref="TransportMetadata"/> —
/// protocol-level resource hierarchy and exposability.
///
/// Consumes <see cref="OwnershipAggregateMetadata"/> and <see cref="EffectTopologyMetadata"/>
/// to determine which entities are exposable as API roots and how they nest.
/// </summary>
internal sealed class TransportPass : INodeAnalyzer {
    public const string Id = "TransportPass";
    public string PassName => Id;
    public string[] Dependencies => [EffectTopologyPass.Id, OwnershipAggregatePass.Id];

    public void Analyze(AnalysisContext context, Node node) {
        if (node is not Domain domain) return;
        if (context.HasStructuralFailure) return;

        var topology = context.GetMetadata<EffectTopologyMetadata>(domain)?.Topology;
        var aggregate = context.GetMetadata<OwnershipAggregateMetadata>(domain)?.Aggregate;

        // Issue 17: Fail closed when metadata is missing
        if (aggregate == null || topology == null) {
            context.ReportDiagnostic(domain,
                DiagnosticSeverity.Error,
                "TransportPass requires EffectTopologyMetadata and OwnershipAggregateMetadata. " +
                "These are produced by the domain analysis pipeline (EffectTopologyPass, OwnershipAggregatePass) " +
                "and must be passed via priorAnalysis.",
                code: "TransportPass.MissingDependency");
            return;
        }

        var transport = BuildTransport(domain, aggregate, topology);
        context.SetMetadata(domain, new TransportMetadata(transport));
    }

    /// <summary>Builds a <see cref="TransportSurface"/> outside the pipeline (for tests/legacy callers).</summary>
    internal static TransportSurface BuildTransport(Domain domain, AggregateModel aggregate, EffectTopology topology) {
        var aggLookup = aggregate.Entities.ToDictionary(e => e.Name, StringComparer.Ordinal);
        var entities = domain.Types.OfType<Entity>().ToList();
        var transportEntities = new List<TransportEntity>(entities.Count);

        foreach (var entity in entities) {
            var agg = aggLookup.GetValueOrDefault(entity.Name);
            var isExposable = agg?.IsRoot ?? false;
            var parentName = agg?.AggregateParentName;
            transportEntities.Add(new TransportEntity(entity.Name, parentName, isExposable));
        }

        return new TransportSurface(domain.Name, transportEntities, topology);
    }
}