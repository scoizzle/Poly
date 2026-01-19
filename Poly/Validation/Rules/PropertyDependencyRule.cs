using Poly.Interpretation;
using Poly.Interpretation.AbstractSyntaxTree;
using Poly.Interpretation.AbstractSyntaxTree.Boolean;
using Poly.Interpretation.AbstractSyntaxTree.Equality;
using static Poly.Interpretation.AbstractSyntaxTree.NodeExtensions;

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

    public override Node BuildInterpretationTree(RuleBuildingContext context)
    {
        var sourceMember = new MemberAccess(context.Value, SourcePropertyName);
        var dependentMember = new MemberAccess(context.Value, DependentPropertyName);

        var sourceHasValue = new NotEqual(sourceMember, Null);
        var dependentHasValue = new NotEqual(dependentMember, Null);

        Node dependencyResult;
        if (RequireWhenSourceHasValue) {
            // If source has value, then dependent must have value
            // !sourceHasValue OR dependentHasValue
            dependencyResult = new Or(new Not(sourceHasValue), dependentHasValue);
        }
        else {
            // If source has value, then dependent must NOT have value
            // !sourceHasValue OR !dependentHasValue
            dependencyResult = new Or(new Not(sourceHasValue), new Not(dependentHasValue));
        }

        return dependencyResult;
    }

    public override string ToString()
    {
        var action = RequireWhenSourceHasValue ? "requires" : "excludes";
        return $"{SourcePropertyName} {action} {DependentPropertyName}";
    }
}