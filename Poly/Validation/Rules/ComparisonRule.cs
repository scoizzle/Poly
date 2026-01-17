using Poly.Interpretation;
using Poly.Interpretation.Expressions;

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

    public override Interpretable BuildInterpretationTree(RuleBuildingContext context)
    {
        var leftProperty = context.TypeDefinition.Properties.FirstOrDefault(p => p.Name == LeftPropertyName)
            ?? throw new InvalidOperationException($"Property '{LeftPropertyName}' not found on type '{context.TypeDefinition.Name}'.");
        var rightProperty = context.TypeDefinition.Properties.FirstOrDefault(p => p.Name == RightPropertyName)
            ?? throw new InvalidOperationException($"Property '{RightPropertyName}' not found on type '{context.TypeDefinition.Name}'.");

        var leftMember = new MemberAccess(context.Value, leftProperty.Name);
        var rightMember = new MemberAccess(context.Value, rightProperty.Name);

        var operationKind = Operator switch {
            ComparisonOperator.Equal => BinaryOperationKind.Equal,
            ComparisonOperator.NotEqual => BinaryOperationKind.NotEqual,
            ComparisonOperator.GreaterThan => BinaryOperationKind.GreaterThan,
            ComparisonOperator.GreaterThanOrEqual => BinaryOperationKind.GreaterThanOrEqual,
            ComparisonOperator.LessThan => BinaryOperationKind.LessThan,
            ComparisonOperator.LessThanOrEqual => BinaryOperationKind.LessThanOrEqual,
            _ => throw new ArgumentException($"Unknown operator: {Operator}")
        };

        return new BinaryOperation(operationKind, leftMember, rightMember);
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