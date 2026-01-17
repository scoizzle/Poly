using Poly.Interpretation;
using Poly.Interpretation.Expressions;

namespace Poly.Validation.Rules;

public enum ArithmeticOperation {
    Add,
    Subtract,
    Multiply,
    Divide
}

public sealed class ComputedValueRule : Rule {
    public string TargetPropertyName { get; set; }
    public string LeftOperandPropertyName { get; set; }
    public ArithmeticOperation Operation { get; set; }
    public string RightOperandPropertyName { get; set; }
    public ComparisonOperator ComparisonOperator { get; set; }

    public ComputedValueRule(
        string targetPropertyName,
        string leftOperandPropertyName,
        ArithmeticOperation operation,
        string rightOperandPropertyName,
        ComparisonOperator comparisonOperator = ComparisonOperator.Equal)
    {
        TargetPropertyName = targetPropertyName;
        LeftOperandPropertyName = leftOperandPropertyName;
        Operation = operation;
        RightOperandPropertyName = rightOperandPropertyName;
        ComparisonOperator = comparisonOperator;
    }

    public override Interpretable BuildInterpretationTree(RuleBuildingContext context)
    {
        var targetProperty = context.TypeDefinition.Properties.FirstOrDefault(p => p.Name == TargetPropertyName)
            ?? throw new InvalidOperationException($"Property '{TargetPropertyName}' not found on type '{context.TypeDefinition.Name}'.");
        var leftProperty = context.TypeDefinition.Properties.FirstOrDefault(p => p.Name == LeftOperandPropertyName)
            ?? throw new InvalidOperationException($"Property '{LeftOperandPropertyName}' not found on type '{context.TypeDefinition.Name}'.");
        var rightProperty = context.TypeDefinition.Properties.FirstOrDefault(p => p.Name == RightOperandPropertyName)
            ?? throw new InvalidOperationException($"Property '{RightOperandPropertyName}' not found on type '{context.TypeDefinition.Name}'.");

        var target = new MemberAccess(context.Value, targetProperty.Name);
        var left = new MemberAccess(context.Value, leftProperty.Name);
        var right = new MemberAccess(context.Value, rightProperty.Name);

        var arithmeticKind = Operation switch {
            ArithmeticOperation.Add => BinaryOperationKind.Add,
            ArithmeticOperation.Subtract => BinaryOperationKind.Subtract,
            ArithmeticOperation.Multiply => BinaryOperationKind.Multiply,
            ArithmeticOperation.Divide => BinaryOperationKind.Divide,
            _ => throw new ArgumentException($"Unknown operation: {Operation}")
        };

        var computation = new BinaryOperation(arithmeticKind, left, right);

        var comparisonKind = ComparisonOperator switch {
            ComparisonOperator.Equal => BinaryOperationKind.Equal,
            ComparisonOperator.NotEqual => BinaryOperationKind.NotEqual,
            ComparisonOperator.GreaterThan => BinaryOperationKind.GreaterThan,
            ComparisonOperator.GreaterThanOrEqual => BinaryOperationKind.GreaterThanOrEqual,
            ComparisonOperator.LessThan => BinaryOperationKind.LessThan,
            ComparisonOperator.LessThanOrEqual => BinaryOperationKind.LessThanOrEqual,
            _ => throw new ArgumentException($"Unknown comparison: {ComparisonOperator}")
        };

        return new BinaryOperation(comparisonKind, target, computation);
    }

    public override string ToString()
    {
        var opSymbol = Operation switch {
            ArithmeticOperation.Add => "+",
            ArithmeticOperation.Subtract => "-",
            ArithmeticOperation.Multiply => "*",
            ArithmeticOperation.Divide => "/",
            _ => "?"
        };
        var cmpSymbol = ComparisonOperator switch {
            ComparisonOperator.Equal => "==",
            ComparisonOperator.NotEqual => "!=",
            ComparisonOperator.GreaterThan => ">",
            ComparisonOperator.GreaterThanOrEqual => ">=",
            ComparisonOperator.LessThan => "<",
            ComparisonOperator.LessThanOrEqual => "<=",
            _ => "?"
        };
        return $"{TargetPropertyName} {cmpSymbol} ({LeftOperandPropertyName} {opSymbol} {RightOperandPropertyName})";
    }
}