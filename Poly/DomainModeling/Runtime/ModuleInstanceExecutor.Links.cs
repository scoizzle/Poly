using System.Collections;

using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Meaning;
using Poly.DomainModeling.Ontology;
using Poly.Runtime;

namespace Poly.DomainModeling.Runtime;

/// <summary>
/// Link/unlink over the relationship fields the module owns: a to-many relationship is
/// the collection field <c>_{name}</c>, a to-one is the property <c>{Name}</c>. A link is
/// a bag mutation; the only host-side bookkeeping is the subscriber registry on the
/// watched target, which the module's own <c>Notify…Subscribers</c> body iterates.
/// </summary>
public sealed partial class ModuleInstanceExecutor {
    /// <summary>
    /// Links <paramref name="target"/> to <paramref name="source"/> over
    /// <paramref name="relationshipName"/>. Idempotent: a repeat succeeds with
    /// <see cref="LinkChange.AlreadyLinked"/> and leaves the bag unchanged. A second
    /// link on a to-one source is refused.
    /// </summary>
    public ModuleOutcome Link(string relationshipName, ModuleInstance source, ModuleInstance target) {
        ArgumentException.ThrowIfNullOrEmpty(relationshipName);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        lock (_gate) {
            return Transact(() => {
                RequireRegistered(source);
                RequireRegistered(target);
                var contract = RequireContract(relationshipName, source, target);
                if (IsLinkedCore(contract, source.Bag, target.Bag))
                    return ModuleOutcome.Ok(change: LinkChange.AlreadyLinked);

                if (IsToMany(contract)) {
                    var field = CollectionField(contract);
                    if (source.Bag.TryGetValue(field, out var raw) && raw is IList list)
                        list.Add(target.Bag);
                    else
                        source.Bag[field] = new List<object> { target.Bag };
                }
                else {
                    if (ReadLinked(source.Bag, contract).Any())
                        return ModuleOutcome.Fail(DomainFailure.Link(
                            Relationship.LinkViolationMessage(relationshipName),
                            $"{source.EntityName}.{relationshipName}"));
                    source.Bag[SingularField(contract)] = target.Bag;
                }
                foreach (var field in SubscriberFields(contract))
                    AddSubscriber(target.Bag, field, source.Bag);
                return ModuleOutcome.Ok(change: LinkChange.Linked);
            });
        }
    }

    /// <summary>
    /// Removes the link if present. Unlinking an absent link succeeds with
    /// <see cref="LinkChange.NotLinked"/> and changes nothing. Unlinking the last owner
    /// of an owned target is refused: another source entity sharing the relationship
    /// name is not an owner. Unlinking a required to-one is refused.
    /// </summary>
    public ModuleOutcome Unlink(string relationshipName, ModuleInstance source, ModuleInstance target) {
        ArgumentException.ThrowIfNullOrEmpty(relationshipName);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        lock (_gate) {
            return Transact(() => {
                RequireRegistered(source);
                RequireRegistered(target);
                var contract = RequireContract(relationshipName, source, target);
                if (!IsLinkedCore(contract, source.Bag, target.Bag))
                    return ModuleOutcome.Ok(change: LinkChange.NotLinked);
                if (contract.SourceOwnsTarget && OwnerCount(contract, target.Bag) == 1)
                    return ModuleOutcome.Fail(DomainFailure.Link(
                        Relationship.UnlinkViolationMessage(relationshipName),
                        $"{source.EntityName}.{relationshipName}"));
                // A required to-one is permanent through the host (L-15a); the generated C# has no
                // unlink door, and L-13 lowers this check into the module.
                if (contract.IsRequired)
                    return ModuleOutcome.Fail(DomainFailure.Link(
                        Relationship.RequiredUnlinkViolationMessage(relationshipName),
                        $"{source.EntityName}.{relationshipName}"));

                if (IsToMany(contract)) {
                    var list = (IList)source.Bag[CollectionField(contract)]!;
                    for (var i = list.Count - 1; i >= 0; i--) {
                        if (ReferenceEquals(list[i], target.Bag))
                            list.RemoveAt(i);
                    }
                }
                else {
                    source.Bag[SingularField(contract)] = null;
                }
                foreach (var field in SubscriberFields(contract))
                    RemoveSubscriber(target.Bag, field, source.Bag);
                return ModuleOutcome.Ok(change: LinkChange.Unlinked);
            });
        }
    }

    public bool IsLinked(string relationshipName, ModuleInstance source, ModuleInstance target) {
        ArgumentException.ThrowIfNullOrEmpty(relationshipName);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        lock (_gate) {
            var contract = ContractFor(relationshipName, source.EntityName);
            return contract is not null && IsLinkedCore(contract, source.Bag, target.Bag);
        }
    }

    /// <summary>Outbound: the registered targets <paramref name="source"/> holds over the relationship.</summary>
    public IReadOnlyList<ModuleInstance> LinkedTargets(string relationshipName, ModuleInstance source) {
        ArgumentException.ThrowIfNullOrEmpty(relationshipName);
        ArgumentNullException.ThrowIfNull(source);
        lock (_gate) {
            var contract = ContractFor(relationshipName, source.EntityName);
            if (contract is null)
                return [];
            var targets = new List<ModuleInstance>();
            foreach (var item in ReadLinked(source.Bag, contract)) {
                if (item is not null && _byBag.TryGetValue(item, out var found))
                    targets.Add(found);
            }
            return targets;
        }
    }

    /// <summary>Inbound: the registered sources that hold <paramref name="target"/> over the relationship.</summary>
    public IReadOnlyList<ModuleInstance> LinkedSources(string relationshipName, ModuleInstance target) {
        ArgumentException.ThrowIfNullOrEmpty(relationshipName);
        ArgumentNullException.ThrowIfNull(target);
        lock (_gate) {
            var sources = new List<ModuleInstance>();
            foreach (var owner in _order) {
                var contract = ContractFor(relationshipName, owner.EntityName);
                if (contract is not null
                    && string.Equals(contract.TargetEntityName, target.EntityName, StringComparison.Ordinal)
                    && IsLinkedCore(contract, owner.Bag, target.Bag))
                    sources.Add(owner);
            }
            return sources;
        }
    }

    /// <summary>
    /// Distinct registered children linked to <paramref name="owner"/> through
    /// relationships it owns. A child linked by two owned relationships counts once.
    /// A non-owned link does not count, and neither does an inbound owned edge.
    /// </summary>
    public int OwnedChildCount(ModuleInstance owner) {
        ArgumentNullException.ThrowIfNull(owner);
        lock (_gate) {
            var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
            var count = 0;
            foreach (var contract in ContractsFrom(owner.EntityName)) {
                if (!contract.SourceOwnsTarget)
                    continue;
                foreach (var item in ReadLinked(owner.Bag, contract)) {
                    if (_byBag.ContainsKey(item) && seen.Add(item))
                        count++;
                }
            }
            return count;
        }
    }

    // ── contracts and fields ──────────────────────────────────────────────────

    private IEnumerable<RelationshipContract> ContractsFrom(string sourceEntityName) =>
        (_analysis.GetMetadata<RelationshipContractMetadata>(default)?.Contracts ?? [])
            .Where(c => string.Equals(c.SourceEntityName, sourceEntityName, StringComparison.Ordinal));

    private RelationshipContract? ContractFor(string relationshipName, string sourceEntityName) =>
        ContractsFrom(sourceEntityName).FirstOrDefault(c =>
            string.Equals(c.Name, relationshipName, StringComparison.Ordinal));

    private RelationshipContract RequireContract(
        string relationshipName, ModuleInstance source, ModuleInstance target) {
        var contract = ContractFor(relationshipName, source.EntityName)
            ?? throw new HostException(
                $"Relationship '{relationshipName}' not found on entity '{source.EntityName}'.");
        if (!string.Equals(contract.TargetEntityName, target.EntityName, StringComparison.Ordinal))
            throw new HostException(
                $"Relationship '{relationshipName}' connects '{contract.SourceEntityName}' → "
                + $"'{contract.TargetEntityName}', not '{target.EntityName}'.");
        return contract;
    }

    private static bool IsToMany(RelationshipContract contract) =>
        contract.Cardinality is RelationshipCardinality.OneToMany or RelationshipCardinality.ManyToMany;

    private static string CollectionField(RelationshipContract contract) =>
        "_" + DomainTypeMapping.ToCamelCase(contract.Name);

    private static string SingularField(RelationshipContract contract) =>
        DomainTypeMapping.ToPascalCase(contract.Name);

    /// <summary>Non-null values the relationship field holds on <paramref name="bag"/>.</summary>
    private static IEnumerable<object> ReadLinked(IDictionary<string, object?> bag, RelationshipContract contract) {
        if (IsToMany(contract)) {
            if (bag.TryGetValue(CollectionField(contract), out var raw) && raw is IEnumerable items and not string) {
                foreach (var item in items) {
                    if (item is not null)
                        yield return item;
                }
            }
        }
        else if (bag.TryGetValue(SingularField(contract), out var one) && one is not null) {
            yield return one;
        }
    }

    private static bool IsLinkedCore(
        RelationshipContract contract, IDictionary<string, object?> source, IDictionary<string, object?> target) {
        foreach (var item in ReadLinked(source, contract)) {
            if (ReferenceEquals(item, target))
                return true;
        }
        return false;
    }

    /// <summary>Inbound edges of the contract held by instances of its source entity.</summary>
    private int OwnerCount(RelationshipContract contract, IDictionary<string, object?> target) {
        var count = 0;
        foreach (var owner in _order) {
            if (string.Equals(owner.EntityName, contract.SourceEntityName, StringComparison.Ordinal)
                && IsLinkedCore(contract, owner.Bag, target))
                count++;
        }
        return count;
    }

    // ── subscriber registry ───────────────────────────────────────────────────

    /// <summary>
    /// Registry fields on the target that should list the source for this relationship:
    /// one per watched stage in the source's subscription plan.
    /// </summary>
    private List<string> SubscriberFields(RelationshipContract contract) {
        var source = RequireEntity(contract.SourceEntityName);
        var fields = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void FromPlan(SubscriptionDispatchPlanMetadata plan) {
            if (!plan.ByRelationshipName.TryGetValue(contract.Name, out var entries))
                return;
            foreach (var entry in entries) {
                if (!string.Equals(entry.SourceEntityName, contract.SourceEntityName, StringComparison.Ordinal)
                    || !string.Equals(entry.TargetEntityName, contract.TargetEntityName, StringComparison.Ordinal))
                    continue;
                foreach (var stageName in entry.StageNames) {
                    var field = DomainToCSharpExporter.SubscriberRegistryFieldName(
                        contract.SourceEntityName, contract.Name, stageName);
                    if (seen.Add(field))
                        fields.Add(field);
                }
            }
        }

        FromPlan(_analysis.GetMetadata<SubscriptionDispatchPlanMetadata>(source)
            ?? throw new InvalidOperationException(
                $"Runtime dispatch requires {nameof(SubscriptionDispatchPlanMetadata)} for entity '{source.Name}'."));
        foreach (var stage in source.Stages) {
            FromPlan(_analysis.GetMetadata<SubscriptionDispatchPlanMetadata>(stage)
                ?? throw new InvalidOperationException(
                    $"Runtime dispatch requires {nameof(SubscriptionDispatchPlanMetadata)} for stage '{stage.Name}'."));
        }
        return fields;
    }

    private static void AddSubscriber(IDictionary<string, object?> target, string field, object subscriber) {
        if (target.TryGetValue(field, out var raw) && raw is IList list) {
            foreach (var existing in list) {
                if (ReferenceEquals(existing, subscriber))
                    return;
            }
            list.Add(subscriber);
            return;
        }
        target[field] = new List<object> { subscriber };
    }

    /// <summary>Drops the subscriber. An empty registry becomes null so the Notify body's guard skips.</summary>
    private static void RemoveSubscriber(IDictionary<string, object?> target, string field, object subscriber) {
        if (!target.TryGetValue(field, out var raw) || raw is not IList list)
            return;
        for (var i = list.Count - 1; i >= 0; i--) {
            if (ReferenceEquals(list[i], subscriber))
                list.RemoveAt(i);
        }
        if (list.Count == 0)
            target[field] = null;
    }
}