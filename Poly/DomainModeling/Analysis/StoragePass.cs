using Poly.DomainModeling.Lowering;
using Poly.Syntax.Analysis;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Analysis pass that produces <see cref="StorageMappingMetadata"/> —
/// storage mapping structure: columns, navigations, FKs, keys, table names.
///
/// Consumes <see cref="OwnershipAggregateMetadata"/> and <see cref="EffectTopologyMetadata"/>
/// from prior passes.
/// Depends on <see cref="OwnershipAggregatePass"/> and <see cref="EffectTopologyPass"/>.
/// 
/// Accepts optional <see cref="TypeMappingRegistry"/> and storage conventions
/// from the authoring context (packs configure these).
/// </summary>
internal sealed class StoragePass : INodeAnalyzer {
    public const string Id = "StoragePass";
    public string PassName => Id;
    public string[] Dependencies => [EffectTopologyPass.Id, OwnershipAggregatePass.Id];
    // Note: standalone usage (new StoragePass() + priorAnalysis) bypasses the
    // AnalyzerBuilder and thus avoids the Dependencies check. The runtime
    // fallback to _analysis handles that case.

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

        // The codegen pipeline invalidates all nodes (including domain), which means
        // the AnalysisContext is fresh and inherits no metadata. Fall back to _analysis
        // when context lookup fails — that's the prior domain analysis result.
        var topology = context.GetMetadata<EffectTopologyMetadata>(domain)?.Topology
            ?? _analysis?.GetMetadata<EffectTopologyMetadata>(domain)?.Topology;
        var aggregate = context.GetMetadata<OwnershipAggregateMetadata>(domain)?.Aggregate
            ?? _analysis?.GetMetadata<OwnershipAggregateMetadata>(domain)?.Aggregate;

        // Fail closed: storage requires aggregate and topology metadata.
        if (aggregate == null || topology == null) {
            context.ReportDiagnostic(domain,
                DiagnosticSeverity.Error,
                "StoragePass requires EffectTopologyMetadata and OwnershipAggregateMetadata. " +
                "These are produced by the domain analysis pipeline (EffectTopologyPass, OwnershipAggregatePass) " +
                "and must be passed via priorAnalysis.",
                code: "StoragePass.MissingDependency");
            return;
        }

        var analyzer = new StorageAnalyzer(domain, _analysis, typeMaps: _typeMaps, conventions: _conventions);
        var storage = analyzer.Analyze(aggregate, topology);
        context.SetMetadata(domain, new StorageMappingMetadata(storage));
    }
}