using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling;

public sealed partial record Relationship {
    internal static Domain.MutationStep CreateSetNameMutation(Relationship relationship, string name) {
        var previous = relationship.Name;
        return new Domain.MutationStep(
            nameof(CreateSetNameMutation),
            () => relationship.Name = Guard.ThrowIfNullOrEmpty(name),
            () => relationship.Name = previous);
    }

    internal static Domain.MutationStep CreateSetShapeMutation(
        Relationship relationship,
        IDomainType source,
        IDomainType target,
        RelationshipCardinality cardinality,
        bool sourceOwnsTarget) {
        var previousSource = relationship.Source;
        var previousTarget = relationship.Target;
        var previousCardinality = relationship.Cardinality;
        var previousSourceOwnsTarget = relationship.SourceOwnsTarget;

        return new Domain.MutationStep(
            nameof(CreateSetShapeMutation),
            () => {
                relationship._source = source;
                relationship._target = target;
                relationship._cardinality = cardinality;
                relationship._sourceOwnsTarget = sourceOwnsTarget;
            },
            () => {
                relationship._source = previousSource;
                relationship._target = previousTarget;
                relationship._cardinality = previousCardinality;
                relationship._sourceOwnsTarget = previousSourceOwnsTarget;
            });
    }

    internal static Domain.MutationStep CreateAddPolicyMutation(Relationship relationship, Policy policy)
        => Entity.CreateAddPolicyMutation(relationship, policy);

    internal static Domain.MutationStep CreateRemovePolicyMutation(Relationship relationship, Policy policy)
        => Entity.CreateRemovePolicyMutation(relationship, policy);

    internal static Domain.MutationStep CreateAddStageMutation(Relationship relationship, Stage stage)
        => Entity.CreateAddStageMutation(relationship, stage);

    internal static Domain.MutationStep CreateRemoveStageMutation(Relationship relationship, Stage stage)
        => Entity.CreateRemoveStageMutation(relationship, stage);

    internal static Domain.MutationStep CreateAddPropertyMutation(Relationship relationship, Property property)
        => Entity.CreateAddPropertyMutation(relationship, property);

    internal static Domain.MutationStep CreateRemovePropertyMutation(Relationship relationship, Property property)
        => Entity.CreateRemovePropertyMutation(relationship, property);
}