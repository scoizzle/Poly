namespace Poly.Data.Modeling;

public sealed partial record Event {
    internal static Domain.MutationStep CreateSetNameMutation(Event @event, string name) {
        var previous = @event.Name;
        return new Domain.MutationStep(
            nameof(CreateSetNameMutation),
            () => @event.Name = Guard.ThrowIfNullOrEmpty(name),
            () => @event.Name = previous);
    }

    internal static Domain.MutationStep CreateAddPropertyMutation(Event @event, Property property) {
        return new Domain.MutationStep(
            nameof(CreateAddPropertyMutation),
            () => @event._properties.Add(property),
            () => @event._properties.Remove(property));
    }

    internal static Domain.MutationStep CreateRemovePropertyMutation(Event @event, Property property) {
        return new Domain.MutationStep(
            nameof(CreateRemovePropertyMutation),
            () => @event._properties.Remove(property),
            () => @event._properties.Add(property));
    }
}