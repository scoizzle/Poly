namespace Poly.Data.Modeling;

public sealed partial record Policy {
    internal static Domain.MutationStep CreateSetNameMutation(Policy policy, string name) {
        var previous = policy.Name;
        return new Domain.MutationStep(
            nameof(CreateSetNameMutation),
            () => policy.Name = Guard.ThrowIfNullOrEmpty(name),
            () => policy.Name = previous);
    }

    internal static Domain.MutationStep CreateAddRuleMutation(Policy policy, IPolicyRule rule) {
        return new Domain.MutationStep(
            nameof(CreateAddRuleMutation),
            () => policy._rules.Add(rule),
            () => policy._rules.Remove(rule));
    }

    internal static Domain.MutationStep CreateRemoveRuleMutation(Policy policy, IPolicyRule rule) {
        return new Domain.MutationStep(
            nameof(CreateRemoveRuleMutation),
            () => policy._rules.Remove(rule),
            () => policy._rules.Add(rule));
    }
}