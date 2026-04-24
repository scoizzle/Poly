namespace Poly.Data.Modeling.Effects;

public sealed class PublishEvent : Effect {
    public required Event Event { get; init; }

    // TODO: Add support for event properties
}