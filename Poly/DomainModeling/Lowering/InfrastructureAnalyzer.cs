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

    public InfrastructureAnalyzer(Domain domain, AnalysisResult? analysis = null) {
        _domain = domain;
        _analysis = analysis;
    }

    /// <summary>Computes the full infrastructure model for the domain.</summary>
    public InfrastructureModel Analyze() {
        var topology = EffectTopologyAnalyzer.Scan(_domain);
        var aggregate = new AggregateAnalyzer(_domain, _analysis).Analyze(topology);
        var behavior = new BehaviorAnalyzer(_domain, _analysis).Analyze();
        var transport = new TransportAnalyzer(_domain).Analyze(aggregate, topology);
        var storage = new StorageAnalyzer(_domain, _analysis).Analyze(aggregate, topology);

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