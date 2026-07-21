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
///   4. Transport surface (resource hierarchy — needs aggregate model)
///   5. Storage mapping (columns, navs — needs aggregate model)
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
        // 1. Effect topology — cross-entity effect scanning
        var effects = TransportAnalyzer.ScanEffects(_domain);

        // 2. Aggregate model — ownership hierarchy (needs topology for create-in priority)
        var aggregate = new AggregateAnalyzer(_domain, _analysis).Analyze(effects);

        // 3. Behavior model — action metadata (independent)
        var behavior = new BehaviorAnalyzer(_domain, _analysis).Analyze();

        // 4. Transport surface — resource hierarchy (needs aggregate)
        var transport = new TransportAnalyzer(_domain, _analysis).Analyze(aggregate);

        // 5. Storage mapping — columns, navs, FKs (needs aggregate + topology)
        var storage = new StorageAnalyzer(_domain, _analysis).Analyze(aggregate, effects);

        return new InfrastructureModel(
            _domain.Name,
            storage,
            aggregate,
            behavior,
            transport
        );
    }
}