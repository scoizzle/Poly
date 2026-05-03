using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling.Effects;

public sealed record LinkRelationship(Domain Domain) : Effect(Domain) {
    public required Relationship Relationship { get; init; }
    public required DomainValue Target { get; init; }
}