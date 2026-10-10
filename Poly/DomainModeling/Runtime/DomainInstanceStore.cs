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
///   <item><see cref="Link"/> adds the source to the target's subscriber
///     registry for each watched stage on that relationship;
///     <see cref="Unlink"/> / <see cref="Remove"/> drop it.</item>
///   <item>A stage transition calls <c>Notify{Stage}Subscribers</c> on the
///     transitioned instance (or a leftover <c>TransitionStage</c> helper
///     calls <see cref="NotifyTransition"/> directly).</item>
///   <item><see cref="NotifyTransition"/> runs that instance's compiled
///     <c>Notify{Stage}Subscribers</c> body, which iterates the registry
///     and calls <c>sub.When…</c>. Any/All set conditions live in those
///     handler bodies (the same trees print emits).</item>
///   <item>A <c>transition</c> inside a handler emits
///     <c>Notify{Target}Subscribers</c>, so the next hop is the compiled
///     tree, not a store recurse.</item>
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
        error = DomainEntityInstance.CreateCheckFailure(target, scalars, creator.Domain);
        return error is null ? DomainResult.Success() : DomainResult.Failure(error);
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
        DomainEntityInstance child;
        try {
            child = DomainEntityInstance.Create(targetEntity, scalars, creator.Domain);
        }
        catch (ConstraintFailureException ex) {
            return DomainResult.Failure(ex.Message);
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

    /// <summary>Removes an instance (e.g. after delete effect). Also drops its links and subscriber-registry entries.</summary>
    public void Remove(DomainEntityInstance instance) {
        var drop = new List<(string RelationshipName, DomainEntityInstance Source, DomainEntityInstance Target)>();
        foreach (var l in _links) {
            if (ReferenceEquals(l.Source, instance) || ReferenceEquals(l.Target, instance))
                drop.Add(l);
        }
        foreach (var l in drop)
            Unlink(l.RelationshipName, l.Source, l.Target);
        instance.Store = null;
        _instances.Remove(instance);
    }

    /// <summary>
    /// Records an instance-level edge for <paramref name="relationshipName"/>
    /// from <paramref name="source"/> to <paramref name="target"/>.
    /// Both instances must already be registered in this store.
    /// A second outbound link on a OneToOne or ManyToOne source throws.
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
        if (source.Domain is not null) {
            var contracts = RuntimeAnalysisCache.GetOrAnalyze(source.Domain)
                .GetMetadata<RelationshipContractMetadata>(default);
            var contract = contracts?.Contracts.FirstOrDefault(c =>
                string.Equals(c.Name, relationshipName, StringComparison.Ordinal)
                && string.Equals(c.SourceEntityName, source.Entity.Name, StringComparison.Ordinal));
            if (contract is not null
                && contract.Cardinality is not (RelationshipCardinality.OneToMany
                    or RelationshipCardinality.ManyToMany)
                && GetLinkedTargets(relationshipName, source).Count > 0)
                throw new InvalidOperationException(
                    Relationship.LinkViolationMessage(relationshipName));
        }
        foreach (var fieldName in SubscriberFieldsForRelationship(relationshipName, source, target))
            target.AddSubscriber(fieldName, source);
        _links.Add((relationshipName, source, target));
    }

    /// <summary>
    /// Removes an instance-level edge if present.
    /// </summary>
    public void Unlink(string relationshipName, DomainEntityInstance source, DomainEntityInstance target) {
        ArgumentException.ThrowIfNullOrEmpty(relationshipName);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        if (!IsLinked(relationshipName, source, target))
            return;
        foreach (var fieldName in SubscriberFieldsForRelationship(relationshipName, source, target))
            target.RemoveSubscriber(fieldName, source);
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
    /// Outbound links only: targets of <paramref name="relationshipName"/> where
    /// <paramref name="source"/> is the link source.
    /// </summary>
    public IReadOnlyList<DomainEntityInstance> GetLinkedTargets(
        string relationshipName, DomainEntityInstance source) {
        ArgumentException.ThrowIfNullOrEmpty(relationshipName);
        ArgumentNullException.ThrowIfNull(source);
        var results = new List<DomainEntityInstance>();
        foreach (var l in _links) {
            if (string.Equals(l.RelationshipName, relationshipName, StringComparison.Ordinal)
                && ReferenceEquals(l.Source, source))
                results.Add(l.Target);
        }
        return results;
    }

    /// <summary>
    /// Called after an instance transitions to a new stage.
    /// Runs that instance's compiled <c>Notify{Stage}Subscribers</c> body.
    /// Subscriber lists come from <see cref="Link"/> / <see cref="Unlink"/> / <see cref="Remove"/>.
    /// </summary>
    /// <param name="transitionedInstance">The instance that changed stage.</param>
    /// <param name="targetStageName">The stage entered.</param>
    public void NotifyTransition(
        DomainEntityInstance transitionedInstance,
        string targetStageName,
        string? previousStageName = null) {
        // Standalone reduced contract: no subscription fan-out without a Domain/catalog.
        var domain = transitionedInstance.Domain;
        if (domain is null) return;

        var analysis = RuntimeAnalysisCache.GetOrAnalyze(domain);
        if (analysis.GetCatalog(domain) is null)
            throw new InvalidOperationException(
                $"Runtime dispatch requires {nameof(DomainCatalogMetadata)} for domain '{domain.Name}' (NotifyTransition).");

        transitionedInstance.ExecuteNotifyStageSubscribers(targetStageName, previousStageName);
    }

    /// <summary>
    /// Registry field names on <paramref name="target"/> that should list
    /// <paramref name="source"/> for this relationship (watched stages on a
    /// matching contract). Empty when the source has no Domain or the
    /// relationship is not a subscription contract.
    /// </summary>
    private static List<string> SubscriberFieldsForRelationship(
        string relationshipName,
        DomainEntityInstance source,
        DomainEntityInstance target) {
        var domain = source.Domain;
        if (domain is null)
            return [];

        var analysis = RuntimeAnalysisCache.GetOrAnalyze(domain);
        var relationshipContracts = analysis.GetMetadata<RelationshipContractMetadata>(default)
            ?? throw new InvalidOperationException(
                $"Runtime dispatch requires {nameof(RelationshipContractMetadata)}.");

        var contractMatches = false;
        foreach (var contract in relationshipContracts.Contracts) {
            if (string.Equals(contract.Name, relationshipName, StringComparison.Ordinal)
                && string.Equals(contract.SourceEntityName, source.Entity.Name, StringComparison.Ordinal)
                && string.Equals(contract.TargetEntityName, target.Entity.Name, StringComparison.Ordinal)) {
                contractMatches = true;
                break;
            }
        }
        if (!contractMatches)
            return [];

        var fields = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void FromPlan(SubscriptionDispatchPlanMetadata plan) {
            if (!plan.ByRelationshipName.TryGetValue(relationshipName, out var entries))
                return;
            foreach (var entry in entries) {
                if (!string.Equals(entry.SourceEntityName, source.Entity.Name, StringComparison.Ordinal)
                    || !string.Equals(entry.TargetEntityName, target.Entity.Name, StringComparison.Ordinal))
                    continue;
                foreach (var stageName in entry.StageNames) {
                    var fieldName = DomainEntityInstance.SubscriberRegistryFieldName(
                        source.Entity.Name, relationshipName, stageName);
                    if (seen.Add(fieldName))
                        fields.Add(fieldName);
                }
            }
        }

        var entityPlan = analysis.GetMetadata<SubscriptionDispatchPlanMetadata>(source.Entity)
            ?? throw new InvalidOperationException(
                $"Runtime dispatch requires {nameof(SubscriptionDispatchPlanMetadata)} for entity '{source.Entity.Name}'.");
        FromPlan(entityPlan);
        foreach (var stage in source.Entity.Stages) {
            var stagePlan = analysis.GetMetadata<SubscriptionDispatchPlanMetadata>(stage)
                ?? throw new InvalidOperationException(
                    $"Runtime dispatch requires {nameof(SubscriptionDispatchPlanMetadata)} for stage '{stage.Name}'.");
            FromPlan(stagePlan);
        }
        return fields;
    }
}
