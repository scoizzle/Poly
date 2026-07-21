using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Effects;
using Poly.Syntax.Analysis;

namespace Poly.DomainModeling.Lowering;

/// <summary>
/// Builds <see cref="TransportSurface"/> — the protocol-level resource
/// hierarchy and effect topology.
///
/// Effect topology scanning (cross-entity create-in, invoke, subscriptions)
/// is a derived domain fact needed by both aggregate analysis and transport.
/// The resource hierarchy (parent context, exposability) consumes the
/// pre-computed <see cref="AggregateModel"/>.
///
/// Action metadata is NOT built here — use <see cref="BehaviorAnalyzer"/>.
/// </summary>
public sealed class TransportAnalyzer {
    private readonly Domain _domain;
    private readonly List<Entity> _entities;
    private readonly List<Relationship> _relationships;
    private readonly AnalysisResult? _analysis;

    public TransportAnalyzer(Domain domain, AnalysisResult? analysis = null) {
        _domain = domain;
        _analysis = analysis;

        var lookup = analysis?.GetMetadata<DomainTypeLookupMetadata>(default);
        if (lookup is not null) {
            _entities = lookup.Entities.ToList();
        }
        else {
            _entities = domain.Types.OfType<Entity>().ToList();
        }

        _relationships = domain.Relationships.ToList();
    }

    /// <summary>Computes the transport surface — effect topology + resource hierarchy.</summary>
    public TransportSurface Analyze(AggregateModel aggregate) {
        // Pass 1: scan effect topology
        var effects = ScanEffectTopology();

        // Pass 2: build transport entity list from aggregate model
        var aggLookup = aggregate.Entities.ToDictionary(e => e.Name, StringComparer.Ordinal);
        var transportEntities = new List<TransportEntity>();

        foreach (var entity in _entities) {
            var agg = aggLookup.GetValueOrDefault(entity.Name);
            var isExposable = agg?.IsRoot ?? false;
            var parentName = agg?.AggregateParentName;

            var te = new TransportEntity(entity.Name) {
                ParentName = parentName,
                IsExposable = isExposable,
            };
            transportEntities.Add(te);
        }

        return new TransportSurface(_domain.Name, transportEntities, effects);
    }

    // ── Effect topology scanning ──────────────────────────────

    /// <summary>Scans effect topology independently (static, no AnalysisResult needed).</summary>
    public static EffectTopology ScanEffects(Domain domain) {
        var analyzer = new TransportAnalyzer(domain);
        return analyzer.ScanEffectTopology();
    }

    private EffectTopology ScanEffectTopology() {
        var createInRels = new List<CreateInRelation>();
        var crossInvokes = new List<CrossEntityInvoke>();
        var subscriptions = new List<SubscriptionRelation>();

        var allActions = new List<(Entity Entity, DomainModeling.Action Action)>();
        foreach (var entity in _entities) {
            foreach (var action in entity.Actions)
                allActions.Add((entity, action));
            foreach (var stage in entity.Stages)
                foreach (var action in stage.Actions)
                    allActions.Add((entity, action));
        }

        foreach (var (entity, action) in allActions) {
            ScanActionEffects(entity, action, createInRels, crossInvokes);
        }

        foreach (var entity in _entities) {
            foreach (var sub in entity.Subscriptions) {
                foreach (var stageName in sub.StageNames) {
                    subscriptions.Add(new SubscriptionRelation(
                        entity.Name, sub.RelationshipName, stageName));
                }
            }
            foreach (var stage in entity.Stages) {
                foreach (var sub in stage.Subscriptions) {
                    foreach (var stageName in sub.StageNames) {
                        subscriptions.Add(new SubscriptionRelation(
                            entity.Name, sub.RelationshipName, stageName));
                    }
                }
            }
        }

        return new EffectTopology(createInRels, crossInvokes, subscriptions);
    }

    private void ScanActionEffects(Entity entity, DomainModeling.Action action,
        List<CreateInRelation> createInRels, List<CrossEntityInvoke> crossInvokes) {
        foreach (var effect in action.Effects) {
            WalkEffects(effect, e => {
                switch (e) {
                    case CreateEntityInRelationshipEffect cir:
                        var createdRel = _relationships.FirstOrDefault(r =>
                            string.Equals(r.Name, cir.RelationshipName, StringComparison.Ordinal));
                        if (createdRel is not null)
                            createInRels.Add(new CreateInRelation(
                                entity.Name, action.Name,
                                cir.RelationshipName, createdRel.Target.TypeName));
                        break;

                    case CreateEntityInstance cei when cei.RelationshipName is not null:
                        createInRels.Add(new CreateInRelation(
                            entity.Name, action.Name,
                            cei.RelationshipName, cei.Type.TypeName));
                        break;

                    case InvokeActionEffect iae when iae.TargetRelationship is not null:
                        crossInvokes.Add(new CrossEntityInvoke(
                            entity.Name, action.Name,
                            iae.TargetRelationship, iae.ActionName));
                        break;
                }
            });
        }
    }

    private static void WalkEffects(Effect effect, Action<Effect> visitor) {
        visitor(effect);
        if (effect is CompositeEffect ce) {
            foreach (var child in ce.Effects) WalkEffects(child, visitor);
        }
        if (effect is ConditionalEffect cond) {
            foreach (var e in cond.ThenEffects) WalkEffects(e, visitor);
            if (cond.ElseEffects is not null)
                foreach (var e in cond.ElseEffects) WalkEffects(e, visitor);
        }
    }
}