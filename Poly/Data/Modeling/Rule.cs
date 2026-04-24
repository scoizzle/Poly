using Poly.Data.Modeling.TypeSystem;
using Poly.Data.Modeling.Validation;

namespace Poly.Data.Modeling;

public abstract class Rule {
    public IDomainValue Value { get; init; } = null!;
    public Constraint Constraints { get; init; }

    public Node ToInterpretationNode(Node parent) {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(Value);
        ArgumentNullException.ThrowIfNull(Value);
        ArgumentNullException.ThrowIfNull(Constraints);

        return parent;
        // var member = parent.GetMember();
        // var constraintNode = Constraints.ToInterpretationNode(member);
        // return constraintNode;
    }
}