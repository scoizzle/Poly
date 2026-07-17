namespace Poly.DomainModeling;

/// <summary>
/// Minimal in-memory store for <see cref="DomainEntityInstance"/> objects.
/// Provides relationship-based lookup for stage-subscription fan-out and
/// <b>instance-level</b> relationship links.
///
/// <para><b>Subscription pipeline:</b></para>
/// <list type="number">
///   <item><c>TransitionStage</c> is called on a <see cref="DomainEntityInstance"/>.</item>
///   <item><c>NotifyTransition</c> iterates registered instances to find subscribers.</item>
///   <item>A subscriber matches if:
///     <list type="bullet">
///       <item>It is in a stage that declares a <see cref="StageSubscription"/>.</item>
///       <item>The subscription's <c>RelationshipName</c> matches a domain relationship
///         where Source = subscriber entity type and Target = transitioned entity type.</item>
///       <item>An <b>instance link</b> exists for that relationship from subscriber → transitioned
///         (see <see cref="Link"/>).</item>
///       <item>The subscription's <c>StageNames</c> includes the target stage.</item>
///       <item>Quantifier is <see cref="StageSubscriptionQuantifier.Each"/> (Any/All deferred).</item>
///     </list>
///   </item>
///   <item>Subscription effects execute on the subscriber with <c>this</c>=subscriber,
///     <c>event</c>=transitioned instance.</item>
///   <item>If a subscriber transitions as a side effect, notification recurses (depth-limited).</item>
/// </list>
///
/// This is intentionally thin — not a full ORM or query engine.
/// Single-relationship hops only (no dotted paths).
/// </summary>
public sealed class DomainInstanceStore {
    private readonly List<DomainEntityInstance> _instances = [];
    private readonly List<(string RelationshipName, DomainEntityInstance Source, DomainEntityInstance Target)> _links = [];

    /// <summary>Registers an instance. Called after creation.</summary>
    public void Add(DomainEntityInstance instance) {
        ArgumentNullException.ThrowIfNull(instance);
        instance.Store = this;
        _instances.Add(instance);
    }

    /// <summary>Removes an instance (e.g. after delete effect). Also drops its links.</summary>
    public void Remove(DomainEntityInstance instance) {
        instance.Store = null;
        _instances.Remove(instance);
        _links.RemoveAll(l =>
            ReferenceEquals(l.Source, instance) || ReferenceEquals(l.Target, instance));
    }

    /// <summary>
    /// Records an instance-level edge for <paramref name="relationshipName"/>
    /// from <paramref name="source"/> to <paramref name="target"/>.
    /// Both instances must already be registered in this store.
    /// </summary>
    public void Link(string relationshipName, DomainEntityInstance source, DomainEntityInstance target) {
        ArgumentException.ThrowIfNullOrEmpty(relationshipName);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        if (!ReferenceEquals(source.Store, this) || !ReferenceEquals(target.Store, this))
            throw new InvalidOperationException(
                "Both instances must be registered in this store before linking.");
        if (IsLinked(relationshipName, source, target))
            return;
        _links.Add((relationshipName, source, target));
    }

    /// <summary>
    /// Removes an instance-level edge if present.
    /// </summary>
    public void Unlink(string relationshipName, DomainEntityInstance source, DomainEntityInstance target) {
        ArgumentException.ThrowIfNullOrEmpty(relationshipName);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        _links.RemoveAll(l =>
            string.Equals(l.RelationshipName, relationshipName, StringComparison.Ordinal)
            && ReferenceEquals(l.Source, source)
            && ReferenceEquals(l.Target, target));
    }

    /// <summary>
    /// Returns whether an instance-level edge exists for the relationship.
    /// </summary>
    public bool IsLinked(string relationshipName, DomainEntityInstance source, DomainEntityInstance target) {
        ArgumentException.ThrowIfNullOrEmpty(relationshipName);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        foreach (var l in _links) {
            if (string.Equals(l.RelationshipName, relationshipName, StringComparison.Ordinal)
                && ReferenceEquals(l.Source, source)
                && ReferenceEquals(l.Target, target))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Called after an instance transitions to a new stage.
    /// Finds subscriber instances whose active subscription matches the transition
    /// <b>and</b> that are instance-linked to the transitioned entity, then runs effects.
    /// </summary>
    /// <param name="transitionedInstance">The instance that changed stage.</param>
    /// <param name="targetStageName">The stage entered.</param>
    /// <param name="depth">Current cascade depth (internal — starts at 0).</param>
    public void NotifyTransition(DomainEntityInstance transitionedInstance, string targetStageName, int depth = 0) {
        const int maxDepth = 10;
        if (depth >= maxDepth) return;

        var domain = transitionedInstance.Domain;
        if (domain is null) return;

        // Find relationships where the transitioned instance is the Target (someone subscribes to it).
        // The subscriber must be the relationship Source (matching SubscriptionContractAnalyzer).
        var incomingRelationships = domain.Relationships
            .Where(r =>
                string.Equals(r.Target.TypeName, transitionedInstance.Entity.Name, StringComparison.Ordinal))
            .ToList();

        if (incomingRelationships.Count == 0) return;

        // For each subscriber instance, check if any of its active subscriptions match
        foreach (var subscriber in _instances) {
            if (subscriber.IsDeleted) continue;
            if (subscriber.CurrentStage is null) continue;

            // Find the stage the subscriber is currently in
            var subscriberStage = subscriber.Entity.Stages
                .FirstOrDefault(s => string.Equals(s.Name, subscriber.CurrentStage, StringComparison.Ordinal));
            if (subscriberStage is null) continue;

            // Check each subscription on this stage
            foreach (var subscription in subscriberStage.Subscriptions) {
                if (subscription.Quantifier != StageSubscriptionQuantifier.Each) continue; // Any/All deferred

                // Subscription must name a relationship where:
                // - Name matches
                // - Source entity = subscriber entity, Target entity = transitioned instance entity
                var matchingRel = incomingRelationships.FirstOrDefault(r =>
                    string.Equals(r.Name, subscription.RelationshipName, StringComparison.Ordinal) &&
                    string.Equals(r.Source.TypeName, subscriber.Entity.Name, StringComparison.Ordinal));
                if (matchingRel is null) continue;

                // Instance-level link required (BR.4.4)
                if (!IsLinked(matchingRel.Name, subscriber, transitionedInstance))
                    continue;

                // Does the target stage match?
                if (!subscription.StageNames.Any(sn =>
                        string.Equals(sn, targetStageName, StringComparison.Ordinal)))
                    continue;

                // Execute subscription effects in the subscriber's context,
                // with the transitioned instance available as "event"
                subscriber.ExecuteSubscriptionEffects(subscription.Effects, transitionedInstance);

                // Recurse if the subscriber also transitioned as a side effect
                if (depth + 1 < maxDepth && subscriber.CurrentStage != subscriberStage.Name)
                    NotifyTransition(subscriber, subscriber.CurrentStage, depth + 1);
            }
        }
    }
}