using System.Text.Json.Serialization;

namespace Poly.Validation;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "Type")]
[JsonDerivedType(typeof(RangeConstraint), "Range")]
[JsonDerivedType(typeof(NotNullConstraint), "NotNull")]
[JsonDerivedType(typeof(Rules.AndRule), "And")]
[JsonDerivedType(typeof(Rules.OrRule), "Or")]
[JsonDerivedType(typeof(Rules.NotRule), "Not")]
[JsonDerivedType(typeof(Rules.ComparisonRule), "Comparison")]
[JsonDerivedType(typeof(Rules.ConditionalRule), "Conditional")]
[JsonDerivedType(typeof(Rules.PropertyDependencyRule), "PropertyDependency")]
[JsonDerivedType(typeof(Rules.MutualExclusionRule), "MutualExclusion")]
[JsonDerivedType(typeof(Rules.ComputedValueRule), "ComputedValue")]
[JsonDerivedType(typeof(Rules.PropertyConstraintRule), "PropertyConstraint")]
public abstract class Rule {
    public abstract Node BuildInterpretationTree(RuleBuildingContext context);
}