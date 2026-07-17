namespace Poly.DomainModeling;

/// <summary>
/// Minimal in-memory store for <see cref="DomainEntityInstance"/> objects.
/// Provides relationship-based lookup for stage-subscription fan-out.
///
/// <para><b>Subscription pipeline (Slice B):</b></para>
/// <list type="number">
///   <item><c>TransitionStage</c> is called on a <see cref="DomainEntityInstance"/>.</item>
///   <item><c>NotifyTransition</c> iterates all registered instances to find subscribers.</item>
///   <item>A subscriber matches if:
///     <list type="bullet">
///       <item>It is in a stage that declares a <see cref="StageSubscription"/>.</item>
///       <item>The subscription's <c>RelationshipName</c> matches a relationship connecting the subscriber to the transitioned instance.</item>
///       <item>The subscription's <c>StageNames</c> includes the target stage.</item>
///     </list>
///   </item>
///   <item>Subscription effects execute on the subscriber with <c>this</c>=subscriber, <c>event</c>=transitioned instance.</item>
///   <item>If a subscriber transitions as a side effect, the notification recurses (depth-limited).</item>
/// </list>
///
/// This is intentionally thin — not a full ORM or query engine.
/// Slice B vertical: supports single-relationship hops only (no dotted paths; Each quantifier only).
///
/// <para><b>Correlation is type-level, not instance-level.</b>
/// When two instances of the same entity type exist (e.g. two Orders and two Trackers),
/// <em>every</em> Tracker instance may react to <em>every</em> Order's stage transition
/// that matches the relationship name and target stage. There is no per-instance link table.
/// Instance-level links are a post-B feature.</para>
/// </summary>
public sealed class DomainInstanceStore {
    private readonly List<DomainEntityInstance> _instances = [];

    /// <summary>Registers an instance. Called after creation.</summary>
    public void Add(DomainEntityInstance instance) {
        ArgumentNullException.ThrowIfNull(instance);
        instance.Store = this;
        _instances.Add(instance);
    }

    /// <summary>Removes an instance (e.g. after delete effect).</summary>
    public void Remove(DomainEntityInstance instance) {
        instance.Store = null;
        _instances.Remove(instance);
    }

    /// <summary>
    /// Called after an instance transitions to a new stage.
    /// Finds all subscriber instances whose active subscription matches
    /// the transition, and executes their subscription effects.
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