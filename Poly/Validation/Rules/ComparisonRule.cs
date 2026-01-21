using Poly.Interpretation.AbstractSyntaxTree.Comparison;
using Poly.Interpretation.AbstractSyntaxTree.Equality;

namespace Poly.Validation.Rules;

public enum ComparisonOperator {
    Equal,
    NotEqual,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual
}

public sealed class ComparisonRule : Rule {
    public string LeftPropertyName { get; set; }
    public string RightPropertyName { get; set; }
    public ComparisonOperator Operator { get; set; }

    public ComparisonRule(string leftPropertyName, ComparisonOperator op, string rightPropertyName)
    {
        LeftPropertyName = leftPropertyName;
        RightPropertyName = rightPropertyName;
        Operator = op;
    }

    public override Node BuildInterpretationTree(RuleBuildingContext context)
    {
        var leftMember = new MemberAccess(context.Value, LeftPropertyName);
        var rightMember = new MemberAccess(context.Value, RightPropertyName);

        Node comparisonResult = Operator switch {
            ComparisonOperator.Equal => new Equal(leftMember, rightMember),
            ComparisonOperator.NotEqual => new NotEqual(leftMember, rightMember),
            ComparisonOperator.GreaterThan => new GreaterThan(leftMember, rightMember),
            ComparisonOperator.GreaterThanOrEqual => new GreaterThanOrEqual(leftMember, rightMember),
            ComparisonOperator.LessThan => new LessThan(leftMember, rightMember),
            ComparisonOperator.LessThanOrEqual => new LessThanOrEqual(leftMember, rightMember),
            _ => throw new ArgumentException($"Unknown operator: {Operator}")
        };

        return comparisonResult;
    }

    public override string ToString()
    {
        var opSymbol = Operator switch {
            ComparisonOperator.Equal => "==",
            ComparisonOperator.NotEqual => "!=",
            ComparisonOperator.GreaterThan => ">",
            ComparisonOperator.GreaterThanOrEqual => ">=",
            ComparisonOperator.LessThan => "<",
            ComparisonOperator.LessThanOrEqual => "<=",
            _ => "?"
        };
        return $"{LeftPropertyName} {opSymbol} {RightPropertyName}";
    }
}