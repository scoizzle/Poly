using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Lowering;

// Notify core: subscription facts, handler naming, and the shared notify vocabulary.
public sealed partial class DomainToCSharpExporter {
    /// <summary>Collected subscription data for cross-entity notification — populated from the
    /// analysis-published <see cref="SubscriptionDispatchPlanMetadata"/> (the SAME dispatch
    /// plan the runtime consumes), one info per watched target stage.</summary>
    internal sealed record SubscriptionInfo(
        string StageName,
        SubscriptionDispatchPlanEntry Subscription,
        Entity SourceEntity,
        Entity TargetEntity,
        Relationship Relationship,
        string? SubscriberStageName = null
    );

    private static string SubscriberRegistryFieldName(SubscriptionInfo info) =>
        SubscriberRegistryFieldName(info.SourceEntity.Name, info.Relationship.Name, info.StageName);

    /// <summary>Registry field on the watched target that lists subscribers of one stage.</summary>
    internal static string SubscriberRegistryFieldName(
        string sourceEntityName, string relationshipName, string stageName) =>
        $"_{ToCamelCase(sourceEntityName)}{ToPascalCase(relationshipName)}{stageName}Subscribers";

    private static string SubscriberRegisterMethodName(SubscriptionInfo info) =>
        $"Register{info.SourceEntity.Name}{ToPascalCase(info.Relationship.Name)}{info.StageName}Subscriber";

    /// <summary>
    /// Collects the subscription facts for one node's dispatch plan (an entity for
    /// entity-level subscriptions, or a stage for stage-scoped ones) into the target/
    /// subscriber maps used for code generation. The exporter consumes the analysis-
    /// published <see cref="SubscriptionDispatchPlanMetadata"/> rather than re-walking
    /// <see cref="StageSubscription"/> — one description of what fires, shared with the
    /// runtime (no per-site re-derivation drift).
    /// </summary>
    internal static void CollectSubscriptionInfo(
        SubscriptionDispatchPlanMetadata plan,
        Entity subscriber,
        string? subscriberStageName,
        IReadOnlyDictionary<string, Entity> entityLookup,
        List<SubscriptionInfo> subList,
        Dictionary<string, List<SubscriptionInfo>> subscriptionsByTarget) {
        foreach (var entry in plan.ByRelationshipName.Values.SelectMany(e => e)) {
            var rel = subscriber.Navigations.FirstOrDefault(n =>
                string.Equals(n.Name, entry.RelationshipName, StringComparison.Ordinal));
            if (rel is null) continue;
            if (!entityLookup.TryGetValue(entry.TargetEntityName, out var targetEntity))
                continue;

            foreach (var sName in entry.StageNames) {
                var info = new SubscriptionInfo(sName, entry, subscriber, targetEntity, rel, subscriberStageName);
                subList.Add(info);

                if (!subscriptionsByTarget.TryGetValue(targetEntity.Name, out var targetList))
                    subscriptionsByTarget[targetEntity.Name] = targetList = new();
                targetList.Add(info);
            }
        }
    }

    /// <summary>
    /// Assigns each subscription its generated handler method name. The name is
    /// quantifier-aware (<c>WhenAny/WhenAll/WhenEach{Target}{Stage}</c>) and
    /// disambiguated with a <c>_{n}</c> suffix when multiple subscriptions share the
    /// same method signature (same quantifier + peer-binding shape) on the same
    /// relationship+stage — two <c>when Rel Stage</c> (Each) blocks must each fire.
    /// Keyed by reference so even structurally-identical subscriptions get distinct names.
    /// </summary>
    internal static Dictionary<SubscriptionInfo, string> BuildHandlerNames(
        IEnumerable<KeyValuePair<string, List<SubscriptionInfo>>> subscriptionsBySubscriber) {
        var map = new Dictionary<SubscriptionInfo, string>(ReferenceEqualityComparer.Instance);
        foreach (var (_, subList) in subscriptionsBySubscriber) {
            var groups = subList
                .GroupBy(info => (
                    info.StageName,
                    info.TargetEntity.Name,
                    QuantifierName(info.Subscription.Quantifier),
                    HasPeerBinding(info)))
                .ToDictionary(g => g.Key, g => g.ToList());
            foreach (var info in subList) {
                var hasPeer = HasPeerBinding(info);
                var quantifier = QuantifierName(info.Subscription.Quantifier);
                var key = (info.StageName, info.TargetEntity.Name, quantifier, hasPeer);
                var name = $"When{quantifier}{info.TargetEntity.Name}{info.StageName}";
                var group = groups[key];
                if (group.Count == 1) {
                    map[info] = name;
                    continue;
                }
                // Name every handler in a multi-handler group by its effect, not
                // by declaration order (`_2` says nothing).
                var label = EffectLabel(info.Subscription);
                var occurrence = group.TakeWhile(other => !ReferenceEquals(other, info))
                    .Count(other => string.Equals(EffectLabel(other.Subscription), label, StringComparison.Ordinal));
                map[info] = occurrence == 0 ? $"{name}_{label}" : $"{name}_{label}_{occurrence + 1}";
            }
        }
        return map;
    }

    private static string EffectLabel(SubscriptionDispatchPlanEntry subscription) =>
        EffectLabel(subscription.Effects.FirstOrDefault()) ?? "Effect";

    private static string? EffectLabel(Effect? effect) => effect switch {
        AssignEffect { Target: PropertyAccess property } => $"Set{property.Name}",
        InvokeActionEffect invoke => $"Call{invoke.ActionName}",
        StageTransitionEffect transition => $"To{transition.TargetStage.StageName}",
        CreateEntityInstance create => $"Create{create.Type.TypeName}",
        CreateEntityInRelationshipEffect create => $"Create{create.RelationshipName}",
        CompositeEffect composite => composite.Effects.Select(EffectLabel).FirstOrDefault(l => l is not null),
        ConditionalEffect conditional => EffectLabel(conditional.ThenEffects.FirstOrDefault())
            ?? (conditional.ElseEffects is null ? null : EffectLabel(conditional.ElseEffects.FirstOrDefault())),
        _ => null,
    };

    private static string QuantifierName(StageSubscriptionQuantifier quantifier) => quantifier switch {
        StageSubscriptionQuantifier.Any => "Any",
        StageSubscriptionQuantifier.All => "All",
        _ => "Each",
    };

    /// <summary>
    /// Handler takes a peer argument when the subscription binds <c>when … as name</c>.
    /// Print (overload key and call args) and simulate (When arm) share this.
    /// </summary>
    internal static bool HasPeerBinding(SubscriptionInfo info) =>
        info.Subscription.PeerBinding is { Length: > 0 };

    /// <summary>
    /// <c>when all</c> handlers take the transitioning record's previous stage.
    /// Print (call args and handler params) and simulate (When arm) share this.
    /// </summary>
    internal static bool NeedsAllPreviousStage(SubscriptionInfo info) =>
        info.Subscription.Quantifier == StageSubscriptionQuantifier.All
        && info.TargetEntity.Stages.Count > 0
        && info.Subscription.StageNames.Count > 0;

    /// <summary>
    /// True when the target entity's <c>Notify{Stage}Subscribers</c> group carries the
    /// nullable previous-stage parameter: at least one subscription in the domain
    /// targeting <paramref name="targetEntity"/> at <paramref name="stageName"/> is
    /// <c>when all</c>. The signature is per (target, stage), not per relationship.
    /// </summary>
    private static bool StageGroupNeedsPrevious(Entity targetEntity, string stageName, Domain domain) {
        foreach (var source in domain.Types.OfType<Entity>()) {
            var subscriptions = source.Subscriptions
                .Concat(source.Stages.SelectMany(s => s.Subscriptions));
            foreach (var subscription in subscriptions) {
                if (subscription.Quantifier != StageSubscriptionQuantifier.All
                    || !subscription.StageNames.Contains(stageName, StringComparer.Ordinal))
                    continue;
                var navigation = source.Navigations.FirstOrDefault(n =>
                    string.Equals(n.Name, subscription.RelationshipName, StringComparison.Ordinal));
                if (navigation is not null
                    && string.Equals(navigation.Target.TypeName, targetEntity.Name, StringComparison.Ordinal))
                    return true;
            }
        }
        return false;
    }
}