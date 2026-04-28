using Poly.Data.Modeling.Validation;

namespace Poly.Data.Modeling;

public sealed partial record Property {
    internal static Domain.MutationStep CreateSetNameMutation(Property property, string name) {
        var previous = property.Name;
        return new Domain.MutationStep(
            nameof(CreateSetNameMutation),
            () => property.Name = Guard.ThrowIfNullOrEmpty(name),
            () => property.Name = previous);
    }

    internal static Domain.MutationStep CreateAddConstraintMutation(Property property, Constraint constraint) {
        return new Domain.MutationStep(
            nameof(CreateAddConstraintMutation),
            () => property._constraints.Add(constraint),
            () => property._constraints.Remove(constraint));
    }

    internal static Domain.MutationStep CreateRemoveConstraintMutation(Property property, Constraint constraint) {
        return new Domain.MutationStep(
            nameof(CreateRemoveConstraintMutation),
            () => property._constraints.Remove(constraint),
            () => property._constraints.Add(constraint));
    }

    internal static Domain.MutationStep CreateAddPolicyMutation(Property property, Policy policy) {
        return new Domain.MutationStep(
            nameof(CreateAddPolicyMutation),
            () => property._policies.Add(policy),
            () => property._policies.Remove(policy));
    }

    internal static Domain.MutationStep CreateRemovePolicyMutation(Property property, Policy policy) {
        return new Domain.MutationStep(
            nameof(CreateRemovePolicyMutation),
            () => property._policies.Remove(policy),
            () => property._policies.Add(policy));
    }
}