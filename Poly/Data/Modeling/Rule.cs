using Poly.Data.Modeling.TypeSystem;
using Poly.Data.Modeling.Validation;

namespace Poly.Data.Modeling;

public abstract class Rule : IPolicyRule {
    public IDomainValue Value { get; init; } = null!;
    public Constraint Constraints { get; init; } = null!;

    public Node ToInterpretationNode(Node subject) {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(Value);
        ArgumentNullException.ThrowIfNull(Constraints);

        return Value switch {
            Property property => Constraints.ToInterpretationNode(subject.GetMember(property.Name)),
            _ => throw new NotSupportedException($"Rule value type '{Value.GetType().Name}' is not supported.")
        };
    }
}