using Poly.DomainModeling.Ontology;

using Action = Poly.DomainModeling.Ontology.Action;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Analysis pass that produces <see cref="EffectTopologyMetadata"/> —
/// cross-entity effect coupling (create-in, cross-entity invoke, subscriptions).
///
/// Scans actions and subscriptions to build the topology of cross-entity effects.
/// This is a pure domain-fact derivation — not a storage or transport convention.
/// Used by aggregate parent resolution, storage subscription lists, and transport.
/// </summary>
internal sealed class EffectTopologyPass : INodeAnalyzer {
    public const string Id = "EffectTopologyPass";
    public string PassName => Id;
    // Pure domain-tree scan; no upstream analysis bags.
    public string[] Dependencies => [];

    public void Analyze(AnalysisContext context, Node node) {
        if (node is not Domain domain) return;
        if (context.HasStructuralFailure) return;

        var topology = Scan(domain);
        context.SetMetadata(domain, new EffectTopologyMetadata(topology));
    }

    /// <summary>Scans create-in, cross-entity invoke, and subscription effects.</summary>
    internal static EffectTopology Scan(Domain domain) {
        ArgumentNullException.ThrowIfNull(domain);

        var entities = domain.Types.OfType<Entity>().ToList();
        var relationships = entities.SelectMany(e => e.Navigations).ToList();
        var createInRels = new List<CreateInRelation>();
        var crossInvokes = new List<CrossEntityInvoke>();
        var subscriptions = new List<SubscriptionRelation>();

        foreach (var entity in entities) {
            foreach (var action in entity.Actions)
                ScanActionEffects(entity, action, stageName: null, relationships, createInRels, crossInvokes);
            foreach (var stage in entity.Stages)
                foreach (var action in stage.Actions)
                    ScanActionEffects(entity, action, stage.Name, relationships, createInRels, crossInvokes);
        }

        foreach (var entity in entities) {
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

    private static void ScanActionEffects(
        Entity entity,
        Action action,
        string? stageName,
        List<Relationship> relationships,
        List<CreateInRelation> createInRels,
        List<CrossEntityInvoke> crossInvokes) {
        foreach (var effect in action.Effects) {
            WalkEffects(effect, e => {
                switch (e) {
                    case CreateEntityInRelationshipEffect cir: {
                            var createdRel = relationships.FirstOrDefault(r =>
                                string.Equals(r.Name, cir.RelationshipName, StringComparison.Ordinal));
                            if (createdRel is not null)
                                createInRels.Add(new CreateInRelation(
                                    entity.Name, action.Name,
                                    cir.RelationshipName, createdRel.Target.TypeName, stageName));
                            break;
                        }
                    case CreateEntityInstance cei when cei.RelationshipName is not null:
                        createInRels.Add(new CreateInRelation(
                            entity.Name, action.Name,
                            cei.RelationshipName, cei.Type.TypeName, stageName));
                        break;
                    case InvokeActionEffect iae when iae.TargetRelationship is not null:
                        crossInvokes.Add(new CrossEntityInvoke(
                            entity.Name, action.Name,
                            iae.TargetRelationship, iae.ActionName));
                        break;
                    case ForEachInvokeEffect efe:
                        crossInvokes.Add(new CrossEntityInvoke(
                            entity.Name, action.Name,
                            efe.RelationshipName, efe.ActionName));
                        break;
                }
            });
        }
    }

    private static void WalkEffects(Effect effect, System.Action<Effect> visitor) {
        visitor(effect);
        if (effect is CompositeEffect ce) {
            foreach (var child in ce.Effects)
                WalkEffects(child, visitor);
        }
        if (effect is ConditionalEffect cond) {
            foreach (var e in cond.ThenEffects)
                WalkEffects(e, visitor);
            if (cond.ElseEffects is not null)
                foreach (var e in cond.ElseEffects)
                    WalkEffects(e, visitor);
        }
    }
}