using Poly.Syntax.Analysis;

namespace Poly.DomainModeling.Lowering;

/// <summary>
/// Coordinates all infrastructure analyzers to produce a unified
/// <see cref="InfrastructureModel"/> for codegen backends.
///
/// Pipeline:
///   1. Effect topology (cross-entity create-in, invoke, subscriptions)
///   2. Aggregate model (ownership hierarchy — needs topology for create-in priority)
///   3. Behavior model (action metadata — independent, uses AnalysisResult)
///   4. Transport surface (resource hierarchy — needs aggregate; reuses topology)
///   5. Storage mapping (columns, navs, FKs — needs aggregate + topology)
/// </summary>
public sealed class InfrastructureAnalyzer {
    private readonly Domain _domain;
    private readonly AnalysisResult? _analysis;
    private readonly TypeMappingRegistry? _typeMaps;
    private readonly IReadOnlyList<IStorageConvention>? _conventions;

    public InfrastructureAnalyzer(
        Domain domain,
        AnalysisResult? analysis = null,
        TypeMappingRegistry? typeMaps = null,
        IReadOnlyList<IStorageConvention>? conventions = null) {
        _domain = domain;
        _analysis = analysis;
        _typeMaps = typeMaps;
        _conventions = conventions;
    }

    /// <summary>
    /// Computes the full infrastructure model for the domain.
    /// When <paramref name="authoring"/> is provided, its type maps and storage
    /// conventions are threaded into <see cref="StorageAnalyzer"/>.
    /// </summary>
    public InfrastructureModel Analyze(DomainAuthoringContext? authoring = null) {
        // Explicit authoring argument wins over ctor-captured maps/conventions.
        var typeMaps = authoring?.TypeMaps ?? _typeMaps;
        var conventions = authoring?.StorageConventions ?? _conventions;

        var topology = EffectTopologyAnalyzer.Scan(_domain);
        var aggregate = new AggregateAnalyzer(_domain, _analysis).Analyze(topology);
        var behavior = new BehaviorAnalyzer(_domain, _analysis).Analyze();
        var transport = new TransportAnalyzer(_domain).Analyze(aggregate, topology);
        var storage = new StorageAnalyzer(_domain, _analysis, typeMaps, conventions)
            .Analyze(aggregate, topology);

        return new InfrastructureModel(
            _domain.Name,
            topology,
            aggregate,
            behavior,
            storage,
            transport
        );
    }
}