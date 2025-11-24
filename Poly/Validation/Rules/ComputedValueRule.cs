using Poly.Interpretation;
using Poly.Interpretation.Operators;
using Poly.Interpretation.Operators.Arithmetic;
using Poly.Interpretation.Operators.Comparison;
using Poly.Interpretation.Operators.Equality;

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
        ComparisonOperator comparisonOperator = ComparisonOperator.Equal) {
        TargetPropertyName = targetPropertyName;
        LeftOperandPropertyName = leftOperandPropertyName;
        Operation = operation;
        RightOperandPropertyName = rightOperandPropertyName;
        ComparisonOperator = comparisonOperator;
    }

    public override Value BuildInterpretationTree(RuleBuildingContext context) {
        var target = new MemberAccess(context.Value, TargetPropertyName);
        var left = new MemberAccess(context.Value, LeftOperandPropertyName);
        var right = new MemberAccess(context.Value, RightOperandPropertyName);
        
        Value computation = Operation switch {
            ArithmeticOperation.Add => new Add(left, right),
            ArithmeticOperation.Subtract => new Subtract(left, right),
            ArithmeticOperation.Multiply => new Multiply(left, right),
            ArithmeticOperation.Divide => new Divide(left, right),
            _ => throw new ArgumentException($"Unknown operation: {Operation}")
        };
        
        return ComparisonOperator switch {
            ComparisonOperator.Equal => new Equal(target, computation),
            ComparisonOperator.NotEqual => new NotEqual(target, computation),
            ComparisonOperator.GreaterThan => new GreaterThan(target, computation),
            ComparisonOperator.GreaterThanOrEqual => new GreaterThanOrEqual(target, computation),
            ComparisonOperator.LessThan => new LessThan(target, computation),
            ComparisonOperator.LessThanOrEqual => new LessThanOrEqual(target, computation),
            _ => throw new ArgumentException($"Unknown comparison: {ComparisonOperator}")
        };
    }

    public override string ToString() {
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