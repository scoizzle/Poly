using Poly.DomainModeling.Analysis;
using Poly.Syntax.Analysis;

namespace Poly.DomainModeling.Lowering;

/// <summary>
/// Builds <see cref="TransportSurface"/> — the protocol-level resource hierarchy.
///
/// Consumes a pre-computed <see cref="AggregateModel"/> and <see cref="EffectTopology"/>.
/// Action metadata is NOT built here — use <see cref="BehaviorAnalyzer"/>.
/// Topology scanning lives in <see cref="EffectTopologyAnalyzer"/>.
/// </summary>
public sealed class TransportAnalyzer {
    private readonly Domain _domain;
    private readonly List<Entity> _entities;

    public TransportAnalyzer(Domain domain, AnalysisResult? analysis = null) {
        _domain = domain;

        var lookup = analysis?.GetMetadata<DomainTypeLookupMetadata>(default);
        _entities = lookup is not null
            ? lookup.Entities.ToList()
            : domain.Types.OfType<Entity>().ToList();
    }

    /// <summary>Computes the transport surface from aggregate hierarchy + topology.</summary>
    public TransportSurface Analyze(AggregateModel aggregate, EffectTopology topology) {
        var aggLookup = aggregate.Entities.ToDictionary(e => e.Name, StringComparer.Ordinal);
        var transportEntities = new List<TransportEntity>(_entities.Count);

        foreach (var entity in _entities) {
            var agg = aggLookup.GetValueOrDefault(entity.Name);
            // Default convention: roots are exposable; children nest under parents.
            var isExposable = agg?.IsRoot ?? false;
            var parentName = agg?.AggregateParentName;
            transportEntities.Add(new TransportEntity(entity.Name, parentName, isExposable));
        }

        return new TransportSurface(_domain.Name, transportEntities, topology);
    }
}