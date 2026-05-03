using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling.Effects.Mutations;

public sealed record Assign(Domain Domain) : Mutation(Domain) {
    public required DomainValue Target { get; init; }
    public required DomainValue Value { get; init; }
}