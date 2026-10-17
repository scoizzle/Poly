using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Effects;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Reports a collection relationship that a subscription or a <c>for</c> loop reads while
/// nothing in the domain writes it.
///
/// A relationship is written when an action does <c>create in Rel</c> (the runtime store
/// links the child to the source), or when it is the target's aggregate parent
/// relationship — the generated child-create endpoint and the printed <c>Attach</c> call
/// write that edge. Anything else (a peer nav bound as a value, or a link added by a host)
/// leaves the edge empty, so a published dispatch plan cannot fire and a quantifier matches
/// nothing: exactly the shape where analysis used to say "nothing breaks".
///
/// Only <b>collection</b> relationships are judged: a singular edge can be satisfied by a
/// value set at create, so "no <c>create in</c>" does not make it dead.
///
/// Depends on <see cref="EffectTopologyPass"/> (create-in writers) and
/// <see cref="OwnershipAggregatePass"/> (the chosen parent relationship).
/// </summary>
internal sealed class UnwrittenRelationshipPass : INodeAnalyzer {
    public const string Id = "UnwrittenRelationshipPass";
    public string PassName => Id;

    public void Analyze(AnalysisContext context, Node node) {
        if (node is not Domain domain) return;

        var topology = context.GetMetadata<EffectTopologyMetadata>(domain)?.Topology;
        var aggregate = context.GetMetadata<OwnershipAggregateMetadata>(domain)?.Aggregate;
        if (topology is null || aggregate is null) return;

        var written = topology.CreateInRelations
            .Select(c => (c.CreatorEntity, c.RelationshipName))
            .ToHashSet();
        // The aggregate parent relationship is written by the emitter's child-create
        // endpoint and by the printed Attach call, so it is not a dead edge.
        foreach (var child in aggregate.Entities) {
            if (child.IsRoot || child.AggregateParentName is null || child.ParentRelationshipName is null)
                continue;
            written.Add((child.AggregateParentName, child.ParentRelationshipName));
        }

        foreach (var entity in domain.Types.OfType<Entity>()) {
            foreach (var subscription in SubscriptionsOf(entity)) {
                if (IsUnwrittenCollection(entity, subscription.RelationshipName, written))
                    context.ReportWarning(subscription,
                        DeadEdgeMessage(entity, subscription.RelationshipName,
                            $"the subscription on {StageList(subscription)}"),
                        DomainModelDiagnosticCodes.UnwrittenRelationship);
            }

            foreach (var forLoop in ForLoopsOf(entity)) {
                if (IsUnwrittenCollection(entity, forLoop.RelationshipName, written))
                    context.ReportWarning(forLoop,
                        DeadEdgeMessage(entity, forLoop.RelationshipName, "'for' loop"),
                        DomainModelDiagnosticCodes.UnwrittenRelationship);
            }
        }
    }

    /// <summary>
    /// True when <paramref name="entity"/>.<paramref name="relationshipName"/> is a
    /// collection that no <c>create in</c> writes and that is not a target's parent
    /// relationship — i.e. an edge only an explicit link can populate.
    /// </summary>
    private static bool IsUnwrittenCollection(
        Entity entity, string relationshipName, HashSet<(string, string)> written) {
        var rel = entity.Navigations.FirstOrDefault(r =>
            string.Equals(r.Name, relationshipName, StringComparison.Ordinal));
        if (rel is null) return false;
        if (rel.Cardinality is not (RelationshipCardinality.OneToMany or RelationshipCardinality.ManyToMany))
            return false;
        return !written.Contains((entity.Name, relationshipName));
    }

    private static string DeadEdgeMessage(Entity entity, string relationshipName, string reader) =>
        $"'{entity.Name}.{relationshipName}' is read by {reader} but nothing in the domain writes it: "
        + $"no action creates in '{relationshipName}' and it is not the aggregate parent of its target, "
        + "so only an explicit link (link_instances or a host) can populate it — until then it matches nothing.";

    private static string StageList(StageSubscription subscription) =>
        subscription.StageNames.Count == 1
            ? $"'{subscription.StageNames[0]}'"
            : $"'" + string.Join("', '", subscription.StageNames) + "'";

    private static IEnumerable<StageSubscription> SubscriptionsOf(Entity entity) {
        foreach (var subscription in entity.Subscriptions)
            yield return subscription;
        foreach (var stage in entity.Stages) {
            foreach (var subscription in stage.Subscriptions)
                yield return subscription;
        }
    }

    private static IEnumerable<ForEachInvokeEffect> ForLoopsOf(Entity entity) {
        foreach (var effect in EffectsOf(entity)) {
            if (effect is ForEachInvokeEffect forLoop)
                yield return forLoop;
        }
    }

    /// <summary>Every effect an entity can run: its actions, its stages' actions and entry/exit, and subscription bodies.</summary>
    private static IEnumerable<Effect> EffectsOf(Entity entity) {
        foreach (var action in entity.Actions) {
            foreach (var effect in EffectHelpers.FlattenEffects(action.Effects))
                yield return effect;
        }
        foreach (var stage in entity.Stages) {
            foreach (var action in stage.Actions) {
                foreach (var effect in EffectHelpers.FlattenEffects(action.Effects))
                    yield return effect;
            }
            foreach (var effect in EffectHelpers.FlattenEffects(stage.OnEntryEffects))
                yield return effect;
            foreach (var effect in EffectHelpers.FlattenEffects(stage.OnExitEffects))
                yield return effect;
            foreach (var subscription in stage.Subscriptions) {
                foreach (var effect in EffectHelpers.FlattenEffects(subscription.Effects))
                    yield return effect;
            }
        }
        foreach (var subscription in entity.Subscriptions) {
            foreach (var effect in EffectHelpers.FlattenEffects(subscription.Effects))
                yield return effect;
        }
    }
}