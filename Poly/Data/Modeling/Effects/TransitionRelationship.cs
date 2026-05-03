using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling.Effects;

public sealed record TransitionRelationship(Domain Domain) : Effect(Domain) {
    public required Relationship Relationship { get; init; }
    public required Stage TargetStage { get; init; }
}