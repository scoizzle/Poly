using Poly.Interpretation;
using Poly.Interpretation.Expressions;
using Poly.Introspection;

namespace Poly.Validation;

public sealed class LengthConstraint(int? minLength, int? maxLength) : Constraint {
    public int? MinLength { get; set; } = minLength;
    public int? MaxLength { get; set; } = maxLength;

    public override Interpretable BuildInterpretationTree(RuleBuildingContext context)
    {
        var lengthProperty = context.TypeDefinition.Properties.FirstOrDefault(p => p.Name == "Length")
            ?? throw new InvalidOperationException($"Type '{context.TypeDefinition.Name}' does not have a Length property.");

        var length = new MemberAccess(context.Value, lengthProperty.Name);

        var minCheck = MinLength.HasValue
            ? new BinaryOperation(BinaryOperationKind.GreaterThanOrEqual, length, new Constant(MinLength.Value))
            : null;

        var maxCheck = MaxLength.HasValue
            ? new BinaryOperation(BinaryOperationKind.LessThanOrEqual, length, new Constant(MaxLength.Value))
            : null;

        var lengthCheck = (minCheck, maxCheck) switch {
            (Interpretable min, Interpretable max) => new BinaryOperation(BinaryOperationKind.And, min, max),
            (Interpretable min, null) => min,
            (null, Interpretable max) => max,
            _ => new Constant(true)
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