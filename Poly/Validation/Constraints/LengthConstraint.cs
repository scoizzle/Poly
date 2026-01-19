using Poly.Interpretation;
using Poly.Interpretation.AbstractSyntaxTree;
using Poly.Interpretation.AbstractSyntaxTree.Boolean;
using Poly.Interpretation.AbstractSyntaxTree.Comparison;
using static Poly.Interpretation.AbstractSyntaxTree.NodeExtensions;

namespace Poly.Validation;

public sealed class LengthConstraint(int? minLength, int? maxLength) : Constraint {
    public int? MinLength { get; set; } = minLength;
    public int? MaxLength { get; set; } = maxLength;

    public override Node BuildInterpretationTree(RuleBuildingContext context)
    {
        var length = context.Value.GetMember("Length");

        var minCheck = MinLength.HasValue
            ? new GreaterThanOrEqual(length, Wrap(MinLength.Value))
            : null;

        var maxCheck = MaxLength.HasValue
            ? new LessThanOrEqual(length, Wrap(MaxLength.Value))
            : null;

        var lengthCheck = (minCheck, maxCheck) switch {
            (Node min, Node max) => new And(min, max),
            (Node min, null) => min,
            (null, Node max) => max,
            _ => Wrap(true)
        };

        return lengthCheck;
    }

    public override string ToString()
    {
        if (MinLength.HasValue && MaxLength.HasValue) {
            return $"value.Length >= {MinLength.Value} && value.Length <= {MaxLength.Value}";
        }
        else if (MinLength.HasValue) {
            return $"value.Length >= {MinLength.Value}";
        }
        else if (MaxLength.HasValue) {
            return $"value.Length <= {MaxLength.Value}";
        }
        else {
            return "true";
        }
    }
}