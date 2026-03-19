using Poly.Interpretation.AbstractSyntaxTree;
using Poly.Interpretation.AbstractSyntaxTree.Arithmetic;
using Poly.Interpretation.AbstractSyntaxTree.Boolean;
using Poly.Interpretation.AbstractSyntaxTree.Comparison;
using Poly.Interpretation.AbstractSyntaxTree.Equality;

namespace Poly.Interpretation.Analysis.ConstantFolding;

/// <summary>
/// Metadata indicating a node's constant-folded value.
/// </summary>
public sealed record ConstantValueMetadata(object? Value) : IAnalysisMetadata;

/// <summary>
/// Performs constant folding optimization by evaluating constant expressions at analysis time.
/// This pass identifies nodes that can be computed at compile time and stores their values.
/// </summary>
public sealed class ConstantFoldingPass : INodeAnalyzer {
    public void Analyze(AnalysisContext context, Node node) {
        // Post-order traversal: analyze children first, then fold parent
        this.AnalyzeChildren(context, node);

        // Try to fold this node if all operands are constants
        var foldedValue = TryFold(context, node);
        if (foldedValue.HasValue) {
            context.SetMetadata(node, new ConstantValueMetadata(foldedValue.Value));
        }
    }

    private FoldResult TryFold(AnalysisContext context, Node node) {
        return node switch {
            Constant c => FoldResult.Success(c.Value),

            // Arithmetic operations
            Add add => FoldBinaryArithmetic(context, add.LeftHandValue, add.RightHandValue, (a, b) => Add(a, b)),
            Subtract sub => FoldBinaryArithmetic(context, sub.LeftHandValue, sub.RightHandValue, (a, b) => Subtract(a, b)),
            Multiply mul => FoldBinaryArithmetic(context, mul.LeftHandValue, mul.RightHandValue, (a, b) => Multiply(a, b)),
            Divide div => FoldBinaryArithmetic(context, div.LeftHandValue, div.RightHandValue, (a, b) => Divide(a, b)),
            Modulo mod => FoldBinaryArithmetic(context, mod.LeftHandValue, mod.RightHandValue, (a, b) => Modulo(a, b)),
            UnaryMinus neg => FoldUnaryArithmetic(context, neg.Operand, Negate),

            // Boolean operations
            And and => FoldBinaryBoolean(context, and.LeftHandValue, and.RightHandValue, (a, b) => a && b),
            Or or => FoldBinaryBoolean(context, or.LeftHandValue, or.RightHandValue, (a, b) => a || b),
            Not not => FoldUnaryBoolean(context, not.Value, a => !a),

            // Comparison operations
            GreaterThan gt => FoldComparison(context, gt.LeftHandValue, gt.RightHandValue, (a, b) => Compare(a, b) > 0),
            GreaterThanOrEqual gte => FoldComparison(context, gte.LeftHandValue, gte.RightHandValue, (a, b) => Compare(a, b) >= 0),
            LessThan lt => FoldComparison(context, lt.LeftHandValue, lt.RightHandValue, (a, b) => Compare(a, b) < 0),
            LessThanOrEqual lte => FoldComparison(context, lte.LeftHandValue, lte.RightHandValue, (a, b) => Compare(a, b) <= 0),

            // Equality operations
            Equal eq => FoldEquality(context, eq.LeftHandValue, eq.RightHandValue, object.Equals),
            NotEqual neq => FoldEquality(context, neq.LeftHandValue, neq.RightHandValue, (a, b) => !object.Equals(a, b)),

            // Conditional with constant condition
            Conditional cond => FoldConditional(context, cond),
            IfStatement ifStmt => FoldIfStatement(context, ifStmt),

            // Coalesce with non-null left
            Coalesce coalesce => FoldCoalesce(context, coalesce),

            _ => FoldResult.NotFoldable
        };
    }

    private FoldResult FoldBinaryArithmetic(AnalysisContext context, Node left, Node right, Func<object, object, object?> operation) {
        var leftValue = GetConstantValue(context, left);
        var rightValue = GetConstantValue(context, right);

        if (!leftValue.HasValue || !rightValue.HasValue)
            return FoldResult.NotFoldable;

        try {
            var result = operation(leftValue.Value!, rightValue.Value!);
            return result != null ? FoldResult.Success(result) : FoldResult.NotFoldable;
        }
        catch {
            return FoldResult.NotFoldable;
        }
    }

    private FoldResult FoldUnaryArithmetic(AnalysisContext context, Node operand, Func<object, object?> operation) {
        var value = GetConstantValue(context, operand);
        if (!value.HasValue)
            return FoldResult.NotFoldable;

        try {
            var result = operation(value.Value!);
            return result != null ? FoldResult.Success(result) : FoldResult.NotFoldable;
        }
        catch {
            return FoldResult.NotFoldable;
        }
    }

    private FoldResult FoldBinaryBoolean(AnalysisContext context, Node left, Node right, Func<bool, bool, bool> operation) {
        var leftValue = GetConstantValue(context, left);
        var rightValue = GetConstantValue(context, right);

        if (!leftValue.HasValue || !rightValue.HasValue)
            return FoldResult.NotFoldable;

        if (leftValue.Value is bool leftBool && rightValue.Value is bool rightBool) {
            return FoldResult.Success(operation(leftBool, rightBool));
        }

        return FoldResult.NotFoldable;
    }

    private FoldResult FoldUnaryBoolean(AnalysisContext context, Node operand, Func<bool, bool> operation) {
        var value = GetConstantValue(context, operand);
        if (!value.HasValue)
            return FoldResult.NotFoldable;

        if (value.Value is bool boolValue) {
            return FoldResult.Success(operation(boolValue));
        }

        return FoldResult.NotFoldable;
    }

    private FoldResult FoldComparison(AnalysisContext context, Node left, Node right, Func<object, object, bool> operation) {
        var leftValue = GetConstantValue(context, left);
        var rightValue = GetConstantValue(context, right);

        if (!leftValue.HasValue || !rightValue.HasValue)
            return FoldResult.NotFoldable;

        try {
            var result = operation(leftValue.Value!, rightValue.Value!);
            return FoldResult.Success(result);
        }
        catch {
            return FoldResult.NotFoldable;
        }
    }

    private FoldResult FoldEquality(AnalysisContext context, Node left, Node right, Func<object?, object?, bool> operation) {
        var leftValue = GetConstantValue(context, left);
        var rightValue = GetConstantValue(context, right);

        if (!leftValue.HasValue || !rightValue.HasValue)
            return FoldResult.NotFoldable;

        return FoldResult.Success(operation(leftValue.Value, rightValue.Value));
    }

    private FoldResult FoldConditional(AnalysisContext context, Conditional cond) {
        var condValue = GetConstantValue(context, cond.Condition);
        if (!condValue.HasValue || condValue.Value is not bool boolCond)
            return FoldResult.NotFoldable;

        // If condition is constant, result is the appropriate branch
        var selectedBranch = boolCond ? cond.IfTrue : cond.IfFalse;
        var branchValue = GetConstantValue(context, selectedBranch);
        return branchValue.HasValue ? FoldResult.Success(branchValue.Value) : FoldResult.NotFoldable;
    }

    private FoldResult FoldIfStatement(AnalysisContext context, IfStatement ifStmt) {
        var condValue = GetConstantValue(context, ifStmt.Condition);
        if (!condValue.HasValue || condValue.Value is not bool boolCond)
            return FoldResult.NotFoldable;

        // If condition is constant true, result is then branch
        // If condition is constant false, result is else branch (if present)
        if (boolCond) {
            var thenValue = GetConstantValue(context, ifStmt.ThenBranch);
            return thenValue.HasValue ? FoldResult.Success(thenValue.Value) : FoldResult.NotFoldable;
        }
        else if (ifStmt.ElseBranch != null) {
            var elseValue = GetConstantValue(context, ifStmt.ElseBranch);
            return elseValue.HasValue ? FoldResult.Success(elseValue.Value) : FoldResult.NotFoldable;
        }

        return FoldResult.NotFoldable;
    }

    private FoldResult FoldCoalesce(AnalysisContext context, Coalesce coalesce) {
        var leftValue = GetConstantValue(context, coalesce.LeftHandValue);
        if (!leftValue.HasValue)
            return FoldResult.NotFoldable;

        // If left is not null, result is left
        if (leftValue.Value != null) {
            return FoldResult.Success(leftValue.Value);
        }

        // If left is null, result is right
        var rightValue = GetConstantValue(context, coalesce.RightHandValue);
        return rightValue.HasValue ? FoldResult.Success(rightValue.Value) : FoldResult.NotFoldable;
    }

    private FoldResult GetConstantValue(AnalysisContext context, Node node) {
        // Check if we already computed a constant value for this node
        var metadata = context.GetMetadata<ConstantValueMetadata>(node);
        if (metadata != null) {
            return FoldResult.Success(metadata.Value);
        }

        // Check if it's a literal constant
        if (node is Constant c) {
            return FoldResult.Success(c.Value);
        }

        return FoldResult.NotFoldable;
    }

    // Arithmetic operations with type coercion
    private static object? Add(object a, object b) => (a, b) switch {
        (int x, int y) => x + y,
        (long x, long y) => x + y,
        (double x, double y) => x + y,
        (float x, float y) => x + y,
        (decimal x, decimal y) => x + y,
        (int x, long y) => x + y,
        (long x, int y) => x + y,
        (int x, double y) => x + y,
        (double x, int y) => x + y,
        (string x, string y) => x + y,
        _ => null
    };

    private static object? Subtract(object a, object b) => (a, b) switch {
        (int x, int y) => x - y,
        (long x, long y) => x - y,
        (double x, double y) => x - y,
        (float x, float y) => x - y,
        (decimal x, decimal y) => x - y,
        (int x, long y) => x - y,
        (long x, int y) => x - y,
        (int x, double y) => x - y,
        (double x, int y) => x - y,
        _ => null
    };

    private static object? Multiply(object a, object b) => (a, b) switch {
        (int x, int y) => x * y,
        (long x, long y) => x * y,
        (double x, double y) => x * y,
        (float x, float y) => x * y,
        (decimal x, decimal y) => x * y,
        (int x, long y) => x * y,
        (long x, int y) => x * y,
        (int x, double y) => x * y,
        (double x, int y) => x * y,
        _ => null
    };

    private static object? Divide(object a, object b) => (a, b) switch {
        (int x, int y) when y != 0 => x / y,
        (long x, long y) when y != 0 => x / y,
        (double x, double y) when y != 0 => x / y,
        (float x, float y) when y != 0 => x / y,
        (decimal x, decimal y) when y != 0 => x / y,
        (int x, long y) when y != 0 => x / y,
        (long x, int y) when y != 0 => x / y,
        (int x, double y) when y != 0 => x / y,
        (double x, int y) when y != 0 => x / y,
        _ => null
    };

    private static object? Modulo(object a, object b) => (a, b) switch {
        (int x, int y) when y != 0 => x % y,
        (long x, long y) when y != 0 => x % y,
        (double x, double y) when y != 0 => x % y,
        (float x, float y) when y != 0 => x % y,
        (decimal x, decimal y) when y != 0 => x % y,
        _ => null
    };

    private static object? Negate(object a) => a switch {
        int x => -x,
        long x => -x,
        double x => -x,
        float x => -x,
        decimal x => -x,
        _ => null
    };

    private static int Compare(object a, object b) {
        if (a is IComparable ca && b is IComparable cb && a.GetType() == b.GetType()) {
            return ca.CompareTo(cb);
        }

        // Try numeric comparison with conversion
        if (TryConvertToDouble(a, out var da) && TryConvertToDouble(b, out var db)) {
            return da.CompareTo(db);
        }

        throw new InvalidOperationException($"Cannot compare {a.GetType()} and {b.GetType()}");
    }

    private static bool TryConvertToDouble(object value, out double result) {
        result = value switch {
            int i => i,
            long l => l,
            double d => d,
            float f => f,
            decimal m => (double)m,
            _ => 0
        };
        return value is int or long or double or float or decimal;
    }

    private readonly struct FoldResult {
        private readonly object? _value;
        public bool HasValue { get; }
        public object? Value => HasValue ? _value : throw new InvalidOperationException("No value");

        private FoldResult(object? value, bool hasValue) {
            _value = value;
            HasValue = hasValue;
        }

        public static FoldResult Success(object? value) => new(value, true);
        public static FoldResult NotFoldable => new(null, false);
    }
}

public static class ConstantFoldingExtensions {
    extension(AnalyzerBuilder builder) {
        /// <summary>
        /// Adds constant folding optimization to the analyzer.
        /// This evaluates constant expressions at analysis time.
        /// </summary>
        public AnalyzerBuilder UseConstantFolding() {
            builder.AddAnalyzer(new ConstantFoldingPass());
            return builder;
        }
    }

    extension(AnalysisContext context) {
        /// <summary>
        /// Gets the constant-folded value for a node, if available.
        /// </summary>
        public object? GetConstantValue(Node node) {
            var metadata = context.GetMetadata<ConstantValueMetadata>(node);
            return metadata?.Value;
        }

        /// <summary>
        /// Returns true if the node has been determined to be a constant.
        /// </summary>
        public bool IsConstant(Node node) {
            return context.GetMetadata<ConstantValueMetadata>(node) != null || node is Constant;
        }
    }

    extension(AnalysisResult result) {
        /// <summary>
        /// Gets the constant-folded value for a node, if available.
        /// </summary>
        public object? GetConstantValue(Node node) {
            var metadata = result.GetMetadata<ConstantValueMetadata>(node);
            return metadata?.Value;
        }

        /// <summary>
        /// Returns true if the node has been determined to be a constant.
        /// </summary>
        public bool IsConstant(Node node) {
            return result.GetMetadata<ConstantValueMetadata>(node) != null || node is Constant;
        }
    }
}