using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling.Effects.Mutations;

public sealed class Assign : Mutation {
    public required IDomainValue Target { get; init; }
    public required IDomainValue Value { get; init; }

    public override void Validate(Entity entity) {
        ArgumentNullException.ThrowIfNull(entity);

        Target.ThrowIfMismatchedDomain(entity.Domain);
        Value.ThrowIfMismatchedDomain(entity.Domain);

        if (!ReferenceEquals(Target.Type, Value.Type)) {
            throw new InvalidOperationException(
                $"Assign effect requires matching types for target and value, but got '{Target.Type.Name}' and '{Value.Type.Name}'.");
        }
    }
}