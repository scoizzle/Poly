using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Runtime;

using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Ontology.Constraints;

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
///       <item>Stage-scoped: it is in a stage that declares a <see cref="StageSubscription"/>, <b>or</b>
///         entity-level: <see cref="Entity.Subscriptions"/> (active regardless of current stage).</item>
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
///   <item><b>Order:</b> stage-scoped handlers first, then entity-level (entity-level dispatch order).</item>
///   <item>Subscription effects execute on the subscriber with <c>this</c>=subscriber,
///     peer bag when <c>PeerBinding</c> is set.</item>
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
        if (!TryAdd(instance, out var error))
            throw new InvalidOperationException(error);
    }

    /// <summary>
    /// Registers an instance or returns the unique-collision message without throwing.
    /// Create-in uses this so duplicate emails become action Failure, not an MCP crash.
    /// </summary>
    public bool TryAdd(DomainEntityInstance instance, out string? error) {
        ArgumentNullException.ThrowIfNull(instance);
        error = UniqueCollisionMessage(instance, except: null);
        if (error is not null)
            return false;
        instance.Store = this;
        _instances.Add(instance);
        return true;
    }

    /// <summary>
    /// Per-property unique check against registered instances. Lowering invokes
    /// this through <see cref="DomainEntityInstance.EnsureUnique"/> so a colliding
    /// assign returns <see cref="DomainResult.Failure"/> without mutating.
    /// Non-unique properties and null values are Success (no peers to collide).
    /// </summary>
    public DomainResult EnsureUnique(
        DomainEntityInstance instance,
        string propertyName,
        object? value) {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentException.ThrowIfNullOrEmpty(propertyName);
        var error = UniqueCollisionForProperty(
            instance.Entity, propertyName, value, except: instance);
        return error is null ? DomainResult.Success() : DomainResult.Failure(error);
    }

    /// <summary>
    /// Allocates <paramref name="typeName"/>, registers it, and returns the child.
    /// Constraint failures and unique collisions are Failure without registering.
    /// Graph wiring (nav initializers) uses <see cref="Link"/> after TryAdd.
    /// </summary>
    public DomainResult Create(
        DomainEntityInstance creator,
        string typeName,
        IReadOnlyDictionary<string, object?> values) {
        ArgumentNullException.ThrowIfNull(creator);
        ArgumentException.ThrowIfNullOrEmpty(typeName);
        ArgumentNullException.ThrowIfNull(values);
        return CreateCore(creator, typeName, values, relationshipName: null);
    }

    /// <summary>
    /// Allocates the relationship target, registers it, and links
    /// <paramref name="source"/> → child on <paramref name="relationshipName"/>.
    /// </summary>
    public DomainResult CreateIn(
        DomainEntityInstance source,
        string relationshipName,
        IReadOnlyDictionary<string, object?> values) {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrEmpty(relationshipName);
        ArgumentNullException.ThrowIfNull(values);
        var domain = source.Domain
            ?? throw new InvalidOperationException(
                "Cannot execute 'create in' effect without a domain to resolve relationship targets.");
        var analysis = RuntimeAnalysisCache.GetOrAnalyze(domain);
        var storage = analysis.GetMetadata<StorageMappingMetadata>(domain);
        var mapped = storage?.Storage.Relationships.FirstOrDefault(r =>
            string.Equals(r.Name, relationshipName, StringComparison.Ordinal)
            && string.Equals(r.SourceType, source.Entity.Name, StringComparison.Ordinal));
        string targetTypeName;
        if (mapped is not null) {
            targetTypeName = mapped.TargetType;
        }
        else {
            var relationship = source.ResolveCreateInRelationship(relationshipName);
            targetTypeName = relationship.Target.TypeName;
        }
        return CreateCore(source, targetTypeName, values, relationshipName);
    }

    /// <summary>
    /// Constraint-checks a prospective create without allocating or linking.
    /// Used as the fail-before-mutate probe prefix in the lowered action tree.
    /// </summary>
    public DomainResult ProbeCreate(
        DomainEntityInstance creator,
        string typeName,
        IReadOnlyDictionary<string, object?> values) {
        ArgumentNullException.ThrowIfNull(creator);
        ArgumentException.ThrowIfNullOrEmpty(typeName);
        ArgumentNullException.ThrowIfNull(values);
        if (!TryResolveTargetEntity(creator, typeName, out var target, out var error)
            || target is null)
            return DomainResult.Failure(error ?? $"Entity type '{typeName}' not found.");
        Dictionary<string, object?> scalars;
        try {
            SplitValues(target, values, out scalars, out _);
        }
        catch (ArgumentException ex) {
            return DomainResult.Failure(ex.Message);
        }
        catch (InvalidOperationException ex) {
            return DomainResult.Failure(ex.Message);
        }
        var validation = DomainEntityInstance.ValidateCreateConstraints(
            target, DomainEntityInstance.FillCreateDefaults(target, scalars, creator.Domain), this);
        return validation is null ? DomainResult.Success() : DomainResult.Failure(validation);
    }

    private DomainResult CreateCore(
        DomainEntityInstance creator,
        string typeName,
        IReadOnlyDictionary<string, object?> values,
        string? relationshipName) {
        if (!TryResolveTargetEntity(creator, typeName, out var targetEntity, out var resolveError)
            || targetEntity is null)
            return DomainResult.Failure(resolveError ?? $"Entity type '{typeName}' not found.");

        Dictionary<string, object?> scalars;
        Dictionary<string, DomainEntityInstance> navs;
        try {
            SplitValues(targetEntity, values, out scalars, out navs);
        }
        catch (ArgumentException ex) {
            return DomainResult.Failure(ex.Message);
        }
        catch (InvalidOperationException ex) {
            return DomainResult.Failure(ex.Message);
        }
        var filled = DomainEntityInstance.FillCreateDefaults(targetEntity, scalars, creator.Domain);
        var uniqueOrConstraint = DomainEntityInstance.ValidateCreateConstraints(
            targetEntity, filled, this);
        if (uniqueOrConstraint is not null)
            return DomainResult.Failure(uniqueOrConstraint);

        DomainEntityInstance child;
        try {
            child = DomainEntityInstance.Create(targetEntity, filled, creator.Domain);
        }
        catch (InvalidOperationException ex) {
            return DomainResult.Failure(ex.Message);
        }
        catch (ArgumentException ex) {
            return DomainResult.Failure(ex.Message);
        }

        creator.TrackCreatedChild(child);
        if (!TryAdd(child, out var addError)) {
            creator.UntrackCreatedChild(child);
            return DomainResult.Failure(addError ?? "Unique constraint violated.");
        }

        try {
            foreach (var (navName, linked) in navs) {
                if (!ReferenceEquals(linked.Store, this)
                    && !TryAdd(linked, out var linkAddError)) {
                    creator.UntrackCreatedChild(child);
                    Remove(child);
                    return DomainResult.Failure(linkAddError ?? "Failed to register linked instance.");
                }
                Link(navName, child, linked);
                creator.TryLinkInverseCollection(linked, child);
            }

            if (relationshipName is not null) {
                if (creator.Domain is not null) {
                    var relationship = creator.ResolveCreateInRelationship(relationshipName);
                    if (!string.Equals(targetEntity.Name, relationship.Target.TypeName, StringComparison.Ordinal)) {
                        creator.UntrackCreatedChild(child);
                        Remove(child);
                        return DomainResult.Failure(
                            $"CreateEntityInstance creates type '{targetEntity.Name}' but relationship " +
                            $"'{relationshipName}' targets '{relationship.Target.TypeName}'.");
                    }
                }
                Link(relationshipName, creator, child);
                creator.TryLinkCreateInBackReference(child);
            }
        }
        catch (InvalidOperationException ex) {
            creator.UntrackCreatedChild(child);
            Remove(child);
            return DomainResult.Failure(ex.Message);
        }

        return DomainResult.Success(child);
    }

    private static bool TryResolveTargetEntity(
        DomainEntityInstance creator,
        string typeName,
        out Entity? target,
        out string? error) {
        target = null;
        error = null;
        if (creator.Domain is not null) {
            var analysis = RuntimeAnalysisCache.GetOrAnalyze(creator.Domain);
            if (!analysis.TryGetEntity(creator.Domain, typeName, out target) || target is null) {
                error = $"Entity type '{typeName}' not found in domain '{creator.Domain.Name}'.";
                return false;
            }
            return true;
        }
        if (!string.Equals(creator.Entity.Name, typeName, StringComparison.Ordinal)) {
            error = $"Entity type '{typeName}' not found.";
            return false;
        }
        target = creator.Entity;
        return true;
    }

    private static void SplitValues(
        Entity targetEntity,
        IReadOnlyDictionary<string, object?> values,
        out Dictionary<string, object?> scalars,
        out Dictionary<string, DomainEntityInstance> navs) {
        var scalarNames = new HashSet<string>(
            targetEntity.Properties.Select(p => p.Name), StringComparer.Ordinal);
        var singularNavs = targetEntity.Navigations
            .Where(n => n.Cardinality is not (RelationshipCardinality.OneToMany
                or RelationshipCardinality.ManyToMany))
            .Select(n => n.Name)
            .ToHashSet(StringComparer.Ordinal);
        scalars = new Dictionary<string, object?>(StringComparer.Ordinal);
        navs = new Dictionary<string, DomainEntityInstance>(StringComparer.Ordinal);
        foreach (var (name, raw) in values) {
            if (scalarNames.Contains(name))
                scalars[name] = raw;
            else if (singularNavs.Contains(name)) {
                if (raw is not DomainEntityInstance linked)
                    throw new InvalidOperationException(
                        $"Create-in initializer '{name}' on '{targetEntity.Name}' must resolve to a linked instance.");
                navs[name] = linked;
            }
            else
                throw new ArgumentException(
                    $"Property '{name}' does not exist on entity '{targetEntity.Name}'. " +
                    $"Available: {string.Join(", ", scalarNames)}.");
        }
    }

    internal void RejectUniqueCollision(DomainEntityInstance candidate, DomainEntityInstance? except) {
        var error = UniqueCollisionMessage(candidate, except);
        if (error is not null)
            throw new InvalidOperationException(error);
    }

    internal string? UniqueCollisionMessage(DomainEntityInstance candidate, DomainEntityInstance? except) =>
        UniqueCollisionMessage(candidate.Entity, proposed: null, except: except, candidate: candidate);

    /// <summary>
    /// Store-aware unique check against a proposed bag (no instance mutate).
    /// <paramref name="candidate"/> is skipped when present (self-assign).
    /// </summary>
    internal string? UniqueCollisionMessage(
        Entity entity,
        IReadOnlyDictionary<string, object?>? proposed,
        DomainEntityInstance? except = null,
        DomainEntityInstance? candidate = null) {
        foreach (var prop in entity.Properties) {
            if (!prop.Constraints.OfType<UniqueConstraint>().Any())
                continue;
            object? value;
            if (proposed is not null) {
                if (!proposed.TryGetValue(prop.Name, out value) || value is null)
                    continue;
            }
            else if (candidate is null || !candidate.TryGetRaw(prop.Name, out value) || value is null) {
                continue;
            }
            var error = UniqueCollisionForProperty(entity, prop.Name, value, except ?? candidate);
            if (error is not null)
                return error;
        }
        return null;
    }

    private string? UniqueCollisionForProperty(
        Entity entity,
        string propertyName,
        object? value,
        DomainEntityInstance? except) {
        if (value is null)
            return null;
        var prop = entity.Properties.FirstOrDefault(p =>
            string.Equals(p.Name, propertyName, StringComparison.Ordinal));
        if (prop is null || !prop.Constraints.OfType<UniqueConstraint>().Any())
            return null;
        foreach (var other in _instances) {
            if (ReferenceEquals(other, except))
                continue;
            if (!string.Equals(other.Entity.Name, entity.Name, StringComparison.Ordinal))
                continue;
            if (!other.TryGetRaw(propertyName, out var otherValue))
                continue;
            if (Equals(otherValue, value))
                return $"Unique constraint violated: '{propertyName}' value is already used on another '{entity.Name}'.";
        }
        return null;
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
    /// Finds subscriber instances whose stage-scoped or entity-level subscription matches
    /// the transition <b>and</b> that are instance-linked to the transitioned entity, then runs effects.
    /// Stage-scoped handlers run first; entity-level handlers run second (same match rules).
    /// </summary>
    /// <param name="transitionedInstance">The instance that changed stage.</param>
    /// <param name="targetStageName">The stage entered.</param>
    /// <param name="depth">Current cascade depth (internal — starts at 0).</param>
    public void NotifyTransition(DomainEntityInstance transitionedInstance, string targetStageName, int depth = 0) {
        const int maxDepth = 10;
        if (depth >= maxDepth) return;

        // Standalone reduced contract: no subscription fan-out without a Domain/catalog.
        var domain = transitionedInstance.Domain;
        if (domain is null) return;

        var analysis = RuntimeAnalysisCache.GetOrAnalyze(domain);
        if (analysis.GetCatalog(domain) is null)
            throw new InvalidOperationException(
                $"Runtime dispatch requires {nameof(DomainCatalogMetadata)} for domain '{domain.Name}' (NotifyTransition).");
        var relationshipContracts = analysis.GetMetadata<RelationshipContractMetadata>(default)
            ?? throw new InvalidOperationException(
                $"Runtime dispatch requires {nameof(RelationshipContractMetadata)}.");

        var incomingContracts = relationshipContracts.Contracts
            .Where(c => string.Equals(c.TargetEntityName, transitionedInstance.Entity.Name, StringComparison.Ordinal))
            .ToList();

        if (incomingContracts.Count == 0) return;

        // For each subscriber: stage-scoped plan first, then always-active entity-level plan.
        foreach (var subscriber in _instances.ToArray()) {

            // --- Stage-scoped (requires resolvable current stage) ---
            if (subscriber.CurrentStage is not null) {
                if (analysis.TryGetStage(subscriber.Entity, subscriber.CurrentStage, out var subscriberStage)
                    && subscriberStage is not null) {
                    var stagePlan = analysis.GetMetadata<SubscriptionDispatchPlanMetadata>(subscriberStage)
                        ?? throw new InvalidOperationException(
                            $"Runtime dispatch requires {nameof(SubscriptionDispatchPlanMetadata)} for stage '{subscriberStage.Name}'.");

                    DispatchMatchingEntries(
                        subscriber,
                        transitionedInstance,
                        targetStageName,
                        stagePlan,
                        incomingContracts,
                        stageNameBeforeEffects: subscriberStage.Name,
                        depth,
                        maxDepth);
                }
            }

            // --- Entity-level (regardless of subscriber current stage) ---
            var entityPlan = analysis.GetMetadata<SubscriptionDispatchPlanMetadata>(subscriber.Entity)
                ?? throw new InvalidOperationException(
                    $"Runtime dispatch requires {nameof(SubscriptionDispatchPlanMetadata)} for entity '{subscriber.Entity.Name}'.");

            DispatchMatchingEntries(
                subscriber,
                transitionedInstance,
                targetStageName,
                entityPlan,
                incomingContracts,
                stageNameBeforeEffects: subscriber.CurrentStage,
                depth,
                maxDepth);
        }
    }

    private void DispatchMatchingEntries(
        DomainEntityInstance subscriber,
        DomainEntityInstance transitionedInstance,
        string targetStageName,
        SubscriptionDispatchPlanMetadata dispatchPlan,
        List<RelationshipContract> incomingContracts,
        string? stageNameBeforeEffects,
        int depth,
        int maxDepth) {
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
                subscriber.ExecuteSubscriptionEffects(entry.Effects, transitionedInstance, entry.PeerBinding);
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
                    t.CurrentStage is not null
                    && entry.StageNames.Any(sn =>
                        string.Equals(sn, t.CurrentStage, StringComparison.Ordinal)));

                // Any: fires once per transition of a linked target into a matching
                // stage (the transitioned instance is always matched — the stage
                // filter above guarantees it). All: fires once when every linked
                // target is in a matching stage — the last one entering triggers it.
                bool shouldFire = entry.Quantifier switch {
                    StageSubscriptionQuantifier.Any => matchedCount >= 1,
                    StageSubscriptionQuantifier.All => matchedCount == allLinkedTargets.Count,
                    _ => false
                };

                if (!shouldFire) continue;
                subscriber.ExecuteSubscriptionEffects(entry.Effects, transitionedInstance, entry.PeerBinding);
            }

            // Recurse if the subscriber also transitioned as a side effect
            if (depth + 1 < maxDepth
                && !string.Equals(subscriber.CurrentStage, stageNameBeforeEffects, StringComparison.Ordinal)
                && subscriber.CurrentStage is not null)
                NotifyTransition(subscriber, subscriber.CurrentStage, depth + 1);
        }
    }
}