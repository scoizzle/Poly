using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling;

public sealed partial record Domain {
    internal static MutationStep CreateSetNameMutation(Domain domain, string name) {
        var previousName = domain.Name;
        return new MutationStep(
            nameof(CreateSetNameMutation),
            () => domain.Name = Guard.ThrowIfNullOrEmpty(name),
            () => domain.Name = previousName);
    }

    internal static MutationStep CreateAddTypeMutation(Domain domain, DomainType type) {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(type);

        return new MutationStep(
            nameof(CreateAddTypeMutation),
            () => domain._types.Add(type),
            () => _ = domain._types.Remove(type));
    }

    internal static MutationStep CreateAddRelationshipMutation(Domain domain, Relationship relationship) {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(relationship);

        return new MutationStep(
            nameof(CreateAddRelationshipMutation),
            () => domain._relationships.Add(relationship),
            () => _ = domain._relationships.Remove(relationship));
    }

}