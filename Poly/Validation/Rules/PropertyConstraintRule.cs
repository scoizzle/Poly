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
        // Create a new context with the property value as the entry point
        var propertyValue = context.GetMemberAccessor(PropertyName);
        var propertyContext = new PropertyConstraintContext(propertyValue, context);
        return Constraint.BuildInterpretationTree(propertyContext);
    }

    public override string ToString() => $"{PropertyName}: {Constraint}";
}

/// <summary>
/// A specialized context for evaluating constraints on a specific property value.
/// </summary>
internal class PropertyConstraintContext : RuleBuildingContext {
    private readonly Value _propertyValue;
    private readonly RuleBuildingContext _parentContext;

    public PropertyConstraintContext(Value propertyValue, RuleBuildingContext parentContext) {
        _propertyValue = propertyValue;
        _parentContext = parentContext;
    }

    public new Value Value => _propertyValue;
}
