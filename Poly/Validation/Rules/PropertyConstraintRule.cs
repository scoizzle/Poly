using Poly.Interpretation;

namespace Poly.Validation.Rules;

/// <summary>
/// Wraps a Constraint to apply it to a specific property in a type-level rule context.
/// </summary>
public sealed class PropertyConstraintRule : Rule {
    public string PropertyName { get; set; }
    public Constraint Constraint { get; set; }

    public PropertyConstraintRule(string propertyName, Constraint constraint) {
        PropertyName = propertyName;
        Constraint = constraint;
    }

    public override Value BuildInterpretationTree(RuleBuildingContext context) {
        var propertyContext = context.GetPropertyContext(PropertyName);
        return Constraint.BuildInterpretationTree(propertyContext);
    }

    public override string ToString() => $"{PropertyName}: {Constraint}";
}