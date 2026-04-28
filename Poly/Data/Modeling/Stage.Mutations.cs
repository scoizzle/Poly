namespace Poly.Data.Modeling;

public sealed partial record Stage {
    internal static Domain.MutationStep CreateSetNameMutation(Stage stage, string name) {
        var previous = stage.Name;
        return new Domain.MutationStep(
            nameof(CreateSetNameMutation),
            () => stage.Name = Guard.ThrowIfNullOrEmpty(name),
            () => stage.Name = previous);
    }

    internal static Domain.MutationStep CreateAddPolicyMutation(Stage stage, Policy policy) {
        return new Domain.MutationStep(
            nameof(CreateAddPolicyMutation),
            () => stage._policies.Add(policy),
            () => stage._policies.Remove(policy));
    }

    internal static Domain.MutationStep CreateRemovePolicyMutation(Stage stage, Policy policy) {
        return new Domain.MutationStep(
            nameof(CreateRemovePolicyMutation),
            () => stage._policies.Remove(policy),
            () => stage._policies.Add(policy));
    }

    internal static Domain.MutationStep CreateAddActionMutation(Stage stage, Action action) {
        return new Domain.MutationStep(
            nameof(CreateAddActionMutation),
            () => stage._actions.Add(action),
            () => stage._actions.Remove(action));
    }

    internal static Domain.MutationStep CreateRemoveActionMutation(Stage stage, Action action) {
        return new Domain.MutationStep(
            nameof(CreateRemoveActionMutation),
            () => stage._actions.Remove(action),
            () => stage._actions.Add(action));
    }
}