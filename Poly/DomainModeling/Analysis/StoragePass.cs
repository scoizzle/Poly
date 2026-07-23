using Poly.DomainModeling.Lowering;
using Poly.Syntax.Analysis;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Analysis pass that produces <see cref="StorageMappingMetadata"/> —
/// storage mapping structure: columns, navigations, FKs, keys, table names.
///
/// Wraps <see cref="StorageAnalyzer"/> as a pass, consuming
/// <see cref="OwnershipAggregateMetadata"/> and <see cref="EffectTopologyMetadata"/>
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

        var topology = context.GetMetadata<EffectTopologyMetadata>(domain)?.Topology;
        var aggregate = context.GetMetadata<OwnershipAggregateMetadata>(domain)?.Aggregate;

        var analyzer = new StorageAnalyzer(domain, _analysis, typeMaps: _typeMaps, conventions: _conventions);
        var storage = analyzer.Analyze(aggregate, topology);
        context.SetMetadata(domain, new StorageMappingMetadata(storage));
    }
}