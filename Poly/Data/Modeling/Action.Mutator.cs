using Poly.Data.Modeling.Effects;

namespace Poly.Data.Modeling;

public sealed partial record Action {

    internal static Domain.MutationStep CreateSetNameMutation(Action action, string name) {
        var previous = action.Name;
        return new Domain.MutationStep(
            nameof(CreateSetNameMutation),
            () => action.Name = Guard.ThrowIfNullOrEmpty(name),
            () => action.Name = previous);
    }

    internal static Domain.MutationStep CreateAddParameterMutation(Action action, Property parameter) {
        return new Domain.MutationStep(
            nameof(CreateAddParameterMutation),
            () => action._parameters.Add(parameter),
            () => action._parameters.Remove(parameter));
    }

    internal static Domain.MutationStep CreateRemoveParameterMutation(Action action, Property parameter) {
        return new Domain.MutationStep(
            nameof(CreateRemoveParameterMutation),
            () => action._parameters.Remove(parameter),
            () => action._parameters.Add(parameter));
    }

    internal static Domain.MutationStep CreateAddEffectMutation(Action action, Effect effect) {
        return new Domain.MutationStep(
            nameof(CreateAddEffectMutation),
            () => action._effects.Add(effect),
            () => action._effects.Remove(effect));
    }

    internal static Domain.MutationStep CreateRemoveEffectMutation(Action action, Effect effect) {
        return new Domain.MutationStep(
            nameof(CreateRemoveEffectMutation),
            () => action._effects.Remove(effect),
            () => action._effects.Add(effect));
    }
}