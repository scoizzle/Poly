namespace Poly.DomainModeling;

using Poly.DomainModeling.Analysis;

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
///       <item>Quantifier is <see cref="StageSubscriptionQuantifier.Each"/> (fires per transition),
///         <see cref="StageSubscriptionQuantifier.Any"/> (fires if any linked target matches), or
///         <see cref="StageSubscriptionQuantifier.All"/> (fires if all linked targets match).</item>
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
    /// Returns the instances linked to <paramref name="instance"/> via <paramref name="relationshipName"/>.
    /// If <paramref name="instance"/> is the relationship Source, returns linked Targets.
    /// If <paramref name="instance"/> is the relationship Target, returns linked Sources.
    /// </summary>
    public IReadOnlyList<DomainEntityInstance> GetRelatedInstances(
        string relationshipName, DomainEntityInstance instance) {
        var results = new List<DomainEntityInstance>();
        foreach (var l in _links) {
            if (!string.Equals(l.RelationshipName, relationshipName, StringComparison.Ordinal))
                continue;
            if (ReferenceEquals(l.Source, instance))
                results.Add(l.Target);
            else if (ReferenceEquals(l.Target, instance))
                results.Add(l.Source);
        }
        return results;
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

        var analysis = RuntimeAnalysisCache.GetOrAnalyze(domain);
        var relationshipContracts = analysis.GetMetadata<RelationshipContractMetadata>(default)
            ?? throw new InvalidOperationException(
                $"Runtime dispatch requires {nameof(RelationshipContractMetadata)}.");

        var incomingContracts = relationshipContracts.Contracts
            .Where(c => string.Equals(c.TargetEntityName, transitionedInstance.Entity.Name, StringComparison.Ordinal))
            .ToList();

        if (incomingContracts.Count == 0) return;

        // For each subscriber instance, check if any of its active subscriptions match
        foreach (var subscriber in _instances) {
            if (subscriber.IsDeleted) continue;
            if (subscriber.CurrentStage is null) continue;

            var subscriberEntityStructure = analysis.GetMetadata<EntityStructureMetadata>(subscriber.Entity);
            if (subscriberEntityStructure is null)
                throw new InvalidOperationException(
                    $"Runtime dispatch requires {nameof(EntityStructureMetadata)} for subscriber entity '{subscriber.Entity.Name}'.");
            if (subscriberEntityStructure.StageByName is null)
                throw new InvalidOperationException(
                    $"Entity '{subscriber.Entity.Name}' has no lifecycle stages; cannot dispatch subscription for current stage '{subscriber.CurrentStage}'.");

            // Subscriber dispatch is best-effort per subscriber: a subscriber whose
            // current stage is not in its own stage set (analysis/instance
            // disagreement) is skipped rather than failing the whole transition
            // (contrast InvokeActionInternal's fail-closed dispatch throw).
            if (!subscriberEntityStructure.StageByName.TryGetValue(subscriber.CurrentStage, out var subscriberStage)) {
                continue;
            }

            var dispatchPlan = analysis.GetMetadata<SubscriptionDispatchPlanMetadata>(subscriberStage)
                ?? throw new InvalidOperationException(
                    $"Runtime dispatch requires {nameof(SubscriptionDispatchPlanMetadata)} for stage '{subscriberStage.Name}'.");

            var applicableEntries = dispatchPlan.ByRelationshipName.Values
                .SelectMany(entries => entries)
                .Where(entry =>
                    string.Equals(entry.SourceEntityName, subscriber.Entity.Name, StringComparison.Ordinal)
                    && string.Equals(entry.TargetEntityName, transitionedInstance.Entity.Name, StringComparison.Ordinal)
                    && incomingContracts.Any(contract =>
                        string.Equals(contract.Name, entry.RelationshipName, StringComparison.Ordinal)
                        && string.Equals(contract.SourceEntityName, entry.SourceEntityName, StringComparison.Ordinal)
                        && string.Equals(contract.TargetEntityName, entry.TargetEntityName, StringComparison.Ordinal)))
                .ToList();

            foreach (var entry in applicableEntries) {
                // Instance-level link required (BR.4.4)
                if (!IsLinked(entry.RelationshipName, subscriber, transitionedInstance))
                    continue;

                // Does the target stage match?
                if (!entry.StageNames.Any(sn =>
                        string.Equals(sn, targetStageName, StringComparison.Ordinal)))
                    continue;

                // Dispatch based on quantifier
                if (entry.Quantifier == StageSubscriptionQuantifier.Each) {
                    // Each: fire effects for every matching transition (default)
                    subscriber.ExecuteSubscriptionEffects(entry.Effects, transitionedInstance);
                }
                else if (entry.Quantifier is StageSubscriptionQuantifier.Any or StageSubscriptionQuantifier.All) {
                    // Any: fire once when at least one related entity is in matching stage.
                    // All: fire once when every related entity is in matching stage.
                    // Both check the current state of all linked targets for that relationship.
                    var allLinkedTargets = _links
                        .Where(l => string.Equals(l.RelationshipName, entry.RelationshipName, StringComparison.Ordinal)
                                 && ReferenceEquals(l.Source, subscriber))
                        .Select(l => l.Target)
                        .ToList();

                    if (allLinkedTargets.Count == 0) continue;

                    var matchedCount = allLinkedTargets.Count(t =>
                        string.Equals(t.CurrentStage, targetStageName, StringComparison.Ordinal));

                    bool shouldFire = entry.Quantifier switch {
                        StageSubscriptionQuantifier.Any => matchedCount >= 1,
                        StageSubscriptionQuantifier.All => matchedCount == allLinkedTargets.Count,
                        _ => false
                    };

                    if (!shouldFire) continue;
                    subscriber.ExecuteSubscriptionEffects(entry.Effects, transitionedInstance);
                }

                // Recurse if the subscriber also transitioned as a side effect
                if (depth + 1 < maxDepth && subscriber.CurrentStage != subscriberStage.Name)
                    NotifyTransition(subscriber, subscriber.CurrentStage, depth + 1);
            }
        }
    }
}