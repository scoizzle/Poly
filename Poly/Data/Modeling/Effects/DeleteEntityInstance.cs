using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling.Effects;

public sealed record DeleteEntityInstance(Domain Domain) : Effect(Domain) {
    public required Entity EntityType { get; init; }
}