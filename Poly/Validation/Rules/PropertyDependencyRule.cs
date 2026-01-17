using Poly.Interpretation;
using Poly.Interpretation.Expressions;

namespace Poly.Validation.Rules;

public sealed class PropertyDependencyRule : Rule {
    public string SourcePropertyName { get; set; }
    public string DependentPropertyName { get; set; }
    public bool RequireWhenSourceHasValue { get; set; }

    public PropertyDependencyRule(string sourcePropertyName, string dependentPropertyName, bool requireWhenSourceHasValue = true)
    {
        SourcePropertyName = sourcePropertyName;
        DependentPropertyName = dependentPropertyName;
        RequireWhenSourceHasValue = requireWhenSourceHasValue;
    }

    public override Interpretable BuildInterpretationTree(RuleBuildingContext context)
    {
        var sourceProperty = context.TypeDefinition.Properties.FirstOrDefault(p => p.Name == SourcePropertyName)
            ?? throw new InvalidOperationException($"Property '{SourcePropertyName}' not found on type '{context.TypeDefinition.Name}'.");
        var dependentProperty = context.TypeDefinition.Properties.FirstOrDefault(p => p.Name == DependentPropertyName)
            ?? throw new InvalidOperationException($"Property '{DependentPropertyName}' not found on type '{context.TypeDefinition.Name}'.");

        var sourceMember = new MemberAccess(context.Value, sourceProperty.Name);
        var dependentMember = new MemberAccess(context.Value, dependentProperty.Name);

        var sourceHasValue = new BinaryOperation(BinaryOperationKind.NotEqual, sourceMember, new Constant(null));
        var dependentHasValue = new BinaryOperation(BinaryOperationKind.NotEqual, dependentMember, new Constant(null));

        Interpretable dependencyResult;
        if (RequireWhenSourceHasValue) {
            // If source has value, then dependent must have value
            // !sourceHasValue OR dependentHasValue
            dependencyResult = new BinaryOperation(
                BinaryOperationKind.Or,
                new UnaryOperation(UnaryOperationKind.Not, sourceHasValue),
                dependentHasValue
            );
        }
        else {
            // If source has value, then dependent must NOT have value
            // !sourceHasValue OR !dependentHasValue
            dependencyResult = new BinaryOperation(
                BinaryOperationKind.Or,
                new UnaryOperation(UnaryOperationKind.Not, sourceHasValue),
                new UnaryOperation(UnaryOperationKind.Not, dependentHasValue)
            );
        }

        return dependencyResult;
    }

    public override string ToString()
    {
        var action = RequireWhenSourceHasValue ? "requires" : "excludes";
        return $"{SourcePropertyName} {action} {DependentPropertyName}";
    }
}