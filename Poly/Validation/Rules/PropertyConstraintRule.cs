using Poly.Interpretation;

namespace Poly.Validation.Rules;

/// <summary>
/// Wraps a Rule to apply it to a specific property in a type-level rule context.
/// </summary>
public sealed class PropertyConstraintRule : Rule {
    public string PropertyName { get; set; }
    public Rule PropertyRule { get; set; }

    public PropertyConstraintRule(string propertyName, Rule propertyRule) {
        PropertyName = propertyName;
        PropertyRule = propertyRule;
    }

    public override Value BuildInterpretationTree(RuleBuildingContext context) {
        var propertyContext = context.GetPropertyContext(PropertyName);
        var propertyRuleResult = PropertyRule.BuildInterpretationTree(propertyContext);
        return propertyRuleResult;
    }

    public override string ToString() => $"{PropertyName}: {PropertyRule}";
}