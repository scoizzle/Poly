using Poly.Data.Modeling.Validation;

namespace Poly.Data.Modeling;

public abstract class Rule {
    public PropertyValueSource Member { get; init; } = null!;
    public Constraint Constraints { get; init; }

    public Node ToInterpretationNode(Node parent) {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(Member);
        ArgumentNullException.ThrowIfNull(Member.Property);
        ArgumentNullException.ThrowIfNull(Constraints);

        var member = parent.GetMember(Member.Property.Name);
        var constraintNode = Constraints.ToInterpretationNode(member);
        return constraintNode;
    }
}