using System.Text.Json.Serialization;

namespace Poly.Validation;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "RuleType")]
[JsonDerivedType(typeof(RangeConstraint), "Range")]
[JsonDerivedType(typeof(NotNullConstraint), "NotNull")]
[JsonDerivedType(typeof(Validation.Rules.AndRule), "And")]
[JsonDerivedType(typeof(Validation.Rules.OrRule), "Or")]
[JsonDerivedType(typeof(Validation.Rules.NotRule), "Not")]
[JsonDerivedType(typeof(Validation.Rules.ComparisonRule), "Comparison")]
[JsonDerivedType(typeof(Validation.Rules.ConditionalRule), "Conditional")]
[JsonDerivedType(typeof(Validation.Rules.PropertyDependencyRule), "PropertyDependency")]
[JsonDerivedType(typeof(Validation.Rules.MutualExclusionRule), "MutualExclusion")]
[JsonDerivedType(typeof(Validation.Rules.ComputedValueRule), "ComputedValue")]
[JsonDerivedType(typeof(Validation.Rules.PropertyConstraintRule), "PropertyConstraint")]
public abstract class Rule {
    public abstract Interpretation.Value BuildInterpretationTree(RuleBuildingContext context);
}

public sealed class RuleEvaluationResult {
    private readonly List<string> _violations = [];

    public IEnumerable<string> Violations => _violations;
    public bool IsValid => _violations.Count == 0;

    public void AddError(string violation) {
        _violations.Add(violation);
    }
}