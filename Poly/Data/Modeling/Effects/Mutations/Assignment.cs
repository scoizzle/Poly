using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling.Effects.Mutations;

public sealed class Assign : Mutation {
    public required IDomainValue Target { get; init; }
    public required IDomainValue Value { get; init; }

    // Validation is now performed by EffectBindingAnalyzer only.
}