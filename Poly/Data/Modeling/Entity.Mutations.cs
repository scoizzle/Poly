namespace Poly.Data.Modeling;

public partial record Entity {
    internal static Domain.MutationStep CreateSetNameMutation(Entity entity, string name) {
        var previous = entity.Name;
        return new Domain.MutationStep(
            nameof(CreateSetNameMutation),
            () => entity.Name = Guard.ThrowIfNullOrEmpty(name),
            () => entity.Name = previous);
    }

    internal static Domain.MutationStep CreateAddPropertyMutation(Entity entity, Property property) {
        return new Domain.MutationStep(
            nameof(CreateAddPropertyMutation),
            () => entity._properties.Add(property),
            () => entity._properties.Remove(property));
    }

    internal static Domain.MutationStep CreateRemovePropertyMutation(Entity entity, Property property) {
        return new Domain.MutationStep(
            nameof(CreateRemovePropertyMutation),
            () => entity._properties.Remove(property),
            () => entity._properties.Add(property));
    }

    internal static Domain.MutationStep CreateAddStageMutation(Entity entity, Stage stage) {
        return new Domain.MutationStep(
            nameof(CreateAddStageMutation),
            () => {
                stage.AttachToEntity(entity);
                entity._stages.Add(stage);
            },
            () => entity._stages.Remove(stage));
    }

    internal static Domain.MutationStep CreateRemoveStageMutation(Entity entity, Stage stage) {
        return new Domain.MutationStep(
            nameof(CreateRemoveStageMutation),
            () => entity._stages.Remove(stage),
            () => {
                stage.AttachToEntity(entity);
                entity._stages.Add(stage);
            });
    }

    internal static Domain.MutationStep CreateAddPolicyMutation(Entity entity, Policy policy) {
        return new Domain.MutationStep(
            nameof(CreateAddPolicyMutation),
            () => entity._policies.Add(policy),
            () => entity._policies.Remove(policy));
    }

    internal static Domain.MutationStep CreateRemovePolicyMutation(Entity entity, Policy policy) {
        return new Domain.MutationStep(
            nameof(CreateRemovePolicyMutation),
            () => entity._policies.Remove(policy),
            () => entity._policies.Add(policy));
    }

    internal static Domain.MutationStep CreateAddActionMutation(Entity entity, Action action) {
        return new Domain.MutationStep(
            nameof(CreateAddActionMutation),
            () => entity._actions.Add(action),
            () => entity._actions.Remove(action));
    }

    internal static Domain.MutationStep CreateRemoveActionMutation(Entity entity, Action action) {
        return new Domain.MutationStep(
            nameof(CreateRemoveActionMutation),
            () => entity._actions.Remove(action),
            () => entity._actions.Add(action));
    }

    internal static Domain.MutationStep CreateAddEventMutation(Entity entity, Event @event) {
        return new Domain.MutationStep(
            nameof(CreateAddEventMutation),
            () => entity._events.Add(@event),
            () => entity._events.Remove(@event));
    }

    internal static Domain.MutationStep CreateRemoveEventMutation(Entity entity, Event @event) {
        return new Domain.MutationStep(
            nameof(CreateRemoveEventMutation),
            () => entity._events.Remove(@event),
            () => entity._events.Add(@event));
    }

    internal static Domain.MutationStep CreateAddRelationshipMutation(Entity entity, Relationship relationship) {
        return new Domain.MutationStep(
            nameof(CreateAddRelationshipMutation),
            () => entity._relationships.Add(relationship),
            () => entity._relationships.Remove(relationship));
    }

    internal static Domain.MutationStep CreateRemoveRelationshipMutation(Entity entity, Relationship relationship) {
        return new Domain.MutationStep(
            nameof(CreateRemoveRelationshipMutation),
            () => entity._relationships.Remove(relationship),
            () => entity._relationships.Add(relationship));
    }
}