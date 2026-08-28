using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Analysis pass that produces <see cref="StorageMappingMetadata"/> —
/// storage mapping structure: columns, navigations, FKs, keys, table names.
///
/// Consumes <see cref="OwnershipAggregateMetadata"/>, <see cref="EffectTopologyMetadata"/>,
/// and <see cref="EntityStructureMetadata"/> from prior passes.
/// Depends on <see cref="OwnershipAggregatePass"/>, <see cref="EffectTopologyPass"/>,
/// and <see cref="EntityStructureAnalyzer"/> (amu-w2-1: storage prefers the precomputed
/// entity-structure bag for key / soft-delete / stage facts over re-scanning).
/// 
/// Accepts optional <see cref="TypeMappingRegistry"/> and storage conventions
/// from the authoring context (packs configure these).
/// </summary>
internal sealed class StoragePass : INodeAnalyzer {
    public const string Id = "StoragePass";
    public string PassName => Id;
    public string[] Dependencies => [EffectTopologyPass.Id, OwnershipAggregatePass.Id, EntityStructureAnalyzer.Id];
    // Standalone `new StoragePass(..., analysis)` bypasses AnalyzerBuilder
    // dependency checks; `_analysis` supplies bags the pipeline would have published.

    private readonly TypeMappingRegistry? _typeMaps;
    private readonly IReadOnlyList<IStorageConvention>? _conventions;
    private readonly AnalysisResult? _analysis;

    public StoragePass(TypeMappingRegistry? typeMaps = null,
        IReadOnlyList<IStorageConvention>? conventions = null,
        AnalysisResult? analysis = null) {
        _typeMaps = typeMaps;
        _conventions = conventions;
        _analysis = analysis;
    }

    public void Analyze(AnalysisContext context, Node node) {
        if (node is not Domain domain) return;
        if (context.HasStructuralFailure) return;

        // Pipeline: bags are on this context. Standalone: fall back to the completed
        // domain analysis passed to the constructor.
        var topology = context.GetMetadata<EffectTopologyMetadata>(domain)?.Topology
            ?? _analysis?.GetMetadata<EffectTopologyMetadata>(domain)?.Topology;
        var aggregate = context.GetMetadata<OwnershipAggregateMetadata>(domain)?.Aggregate
            ?? _analysis?.GetMetadata<OwnershipAggregateMetadata>(domain)?.Aggregate;

        if (aggregate == null || topology == null) {
            context.ReportDiagnostic(domain,
                DiagnosticSeverity.Error,
                "StoragePass requires EffectTopologyMetadata and OwnershipAggregateMetadata. " +
                "These are produced by EffectTopologyPass and OwnershipAggregatePass " +
                "(pipeline) or a completed domain AnalysisResult (standalone).",
                code: "StoragePass.MissingDependency");
            return;
        }

        var typeMaps = _typeMaps;
        var conventions = _conventions;
        if (typeMaps is null && domain.Extensions.Count > 0) {
            var session = RuntimeAnalysisCache.Session(domain);
            typeMaps = session.TypeMaps;
            conventions = session.StorageConventions;
        }

        var analyzer = new StorageAnalyzer(domain, context: context, analysis: _analysis, typeMaps: typeMaps, conventions: conventions);
        var storage = analyzer.Analyze(aggregate, topology);
        context.SetMetadata(domain, new StorageMappingMetadata(storage));
    }
}