using Poly.Interpretation.Analysis.Semantics;

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
        if (!context.TryBeginAnalyzerVisit<ConstantFoldingPass>(node)) {
            return;
        }

        if (node is Constant) {
            return;
        }

        // Post-order traversal: analyze children first, then fold parent
        this.AnalyzeChildren(context, node);

        // Try to fold this node if all operands are constants
        var foldedValue = TryFold(context, node);
        if (foldedValue.HasValue) {
            context.SetMetadata(node, new ConstantValueMetadata(foldedValue.Value));
            var foldedNode = TypeMatchReplacement(node, new Constant(foldedValue.Value), context);
            context.SetNodeReplacement(node, foldedNode);
            return;
        }

        var simplified = TrySimplify(context, node);
        if (simplified != null) {
            var typed = TypeMatchReplacement(node, simplified, context);
            context.SetNodeReplacement(node, typed);
        }
    }

    private FoldResult TryFold(AnalysisContext context, Node node, IReadOnlyDictionary<NodeId, object?>? parameterValues = null) {
        return node switch {
            Constant c => FoldResult.Success(c.Value),
            Parameter parameter => FoldParameter(parameter, parameterValues),

            // Arithmetic operations
            Add add => FoldBinaryArithmetic(context, add.LeftHandValue, add.RightHandValue, Add, parameterValues),
            Subtract sub => FoldBinaryArithmetic(context, sub.LeftHandValue, sub.RightHandValue, Subtract, parameterValues),
            Multiply mul => FoldBinaryArithmetic(context, mul.LeftHandValue, mul.RightHandValue, Multiply, parameterValues),
            Divide div => FoldBinaryArithmetic(context, div.LeftHandValue, div.RightHandValue, Divide, parameterValues),
            Modulo mod => FoldBinaryArithmetic(context, mod.LeftHandValue, mod.RightHandValue, Modulo, parameterValues),
            UnaryMinus neg => FoldUnaryArithmetic(context, neg.Operand, Negate, parameterValues),

            // Boolean operations
            And and => FoldAnd(context, and, parameterValues),
            Or or => FoldOr(context, or, parameterValues),
            Not not => FoldUnaryBoolean(context, not.Value, a => !a, parameterValues),

            // Comparison operations
            GreaterThan gt => FoldComparison(context, gt.LeftHandValue, gt.RightHandValue, (a, b) => Compare(a, b) > 0, parameterValues),
            GreaterThanOrEqual gte => FoldComparison(context, gte.LeftHandValue, gte.RightHandValue, (a, b) => Compare(a, b) >= 0, parameterValues),
            LessThan lt => FoldComparison(context, lt.LeftHandValue, lt.RightHandValue, (a, b) => Compare(a, b) < 0, parameterValues),
            LessThanOrEqual lte => FoldComparison(context, lte.LeftHandValue, lte.RightHandValue, (a, b) => Compare(a, b) <= 0, parameterValues),

            // Equality operations
            Equal eq => FoldEquality(context, eq.LeftHandValue, eq.RightHandValue, object.Equals, parameterValues),
            NotEqual neq => FoldEquality(context, neq.LeftHandValue, neq.RightHandValue, (a, b) => !object.Equals(a, b), parameterValues),

            // Conditional with constant condition
            Conditional cond => FoldConditional(context, cond, parameterValues),
            IfStatement ifStmt => FoldIfStatement(context, ifStmt, parameterValues),

            // Coalesce with non-null left
            Coalesce coalesce => FoldCoalesce(context, coalesce, parameterValues),

            // Lambda invocation with constant arguments
            Invoke invoke => FoldInvocation(context, invoke, parameterValues),

            _ => FoldResult.NotFoldable
        };
    }

    private static FoldResult FoldParameter(Parameter parameter, IReadOnlyDictionary<NodeId, object?>? parameterValues) {
        if (parameterValues != null && parameterValues.TryGetValue(parameter.Id, out var value)) {
            return FoldResult.Success(value);
        }

        return FoldResult.NotFoldable;
    }

    private Node? TrySimplify(AnalysisContext context, Node node) {
        return node switch {
            Add add => SimplifyAddition(context, add),
            Subtract sub => SimplifySubtraction(context, sub),
            Multiply mul => SimplifyMultiplication(context, mul),
            Divide div => SimplifyDivision(context, div),
            Modulo mod => SimplifyModulo(context, mod),
            UnaryMinus neg => SimplifyUnaryMinus(context, neg),
            BitwiseNot bn => SimplifyBitwiseNot(context, bn),
            Not n => SimplifyNot(context, n),
            BitwiseAnd ba => SimplifyBitwiseAnd(context, ba),
            BitwiseOr bo => SimplifyBitwiseOr(context, bo),
            BitwiseXor bx => SimplifyBitwiseXor(context, bx),
            And and => SimplifyAnd(context, and),
            Or or => SimplifyOr(context, or),
            _ => null
        };
    }

    /// <summary>If the replacement node's type differs from the original
    /// node's resolved type, wrap in a <see cref="TypeCast"/> to preserve
    /// the original type, and copy the resolved type onto the cast so
    /// downstream passes see consistent types.</summary>
    private static Node TypeMatchReplacement(Node original, Node replacement, AnalysisContext context) {
        // Only wrap Constant replacements whose value type doesn't match
        if (replacement is not Constant c || c.Value is null)
            return replacement;

        var resolvedType = context.GetResolvedType(original);
        if (resolvedType is null)
            return replacement;

        var valueType = c.Value.GetType();
        var resolvedFullName = resolvedType.FullName;

        string valueTypeName = valueType switch {
            Type t when t == typeof(int) => "System.Int32",
            Type t when t == typeof(long) => "System.Int64",
            Type t when t == typeof(short) => "System.Int16",
            Type t when t == typeof(byte) => "System.Byte",
            Type t when t == typeof(uint) => "System.UInt32",
            Type t when t == typeof(ulong) => "System.UInt64",
            Type t when t == typeof(double) => "System.Double",
            Type t when t == typeof(float) => "System.Single",
            Type t when t == typeof(bool) => "System.Boolean",
            _ => valueType.FullName ?? valueType.Name
        };

        if (valueTypeName == resolvedFullName)
            return replacement;

        // Type mismatch — wrap in a cast to preserve the original type,
        // and propagate the resolved type so backends can lower it.
        var cast = new TypeCast(replacement, new TypeReference(resolvedFullName));
        context.SetResolvedType(cast, resolvedType);
        return cast;
    }

    private static bool IsPowerOf2(long n) => n > 0 && (n & (n - 1)) == 0;

    private FoldResult FoldBinaryArithmetic(AnalysisContext context, Node left, Node right, Func<object, object, object?> operation, IReadOnlyDictionary<NodeId, object?>? parameterValues = null) {
        var leftValue = GetConstantValue(context, left, parameterValues);
        var rightValue = GetConstantValue(context, right, parameterValues);

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

    private FoldResult FoldUnaryArithmetic(AnalysisContext context, Node operand, Func<object, object?> operation, IReadOnlyDictionary<NodeId, object?>? parameterValues = null) {
        var value = GetConstantValue(context, operand, parameterValues);
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

    private FoldResult FoldBinaryBoolean(AnalysisContext context, Node left, Node right, Func<bool, bool, bool> operation, IReadOnlyDictionary<NodeId, object?>? parameterValues = null) {
        var leftValue = GetConstantValue(context, left, parameterValues);
        var rightValue = GetConstantValue(context, right, parameterValues);

        if (!leftValue.HasValue || !rightValue.HasValue)
            return FoldResult.NotFoldable;

        if (leftValue.Value is bool leftBool && rightValue.Value is bool rightBool) {
            return FoldResult.Success(operation(leftBool, rightBool));
        }

        return FoldResult.NotFoldable;
    }

    private FoldResult FoldAnd(AnalysisContext context, And and, IReadOnlyDictionary<NodeId, object?>? parameterValues = null) {
        var leftValue = GetConstantValue(context, and.LeftHandValue, parameterValues);
        if (leftValue.HasValue && leftValue.Value is bool leftBool && !leftBool) {
            return FoldResult.Success(false);
        }

        return FoldBinaryBoolean(context, and.LeftHandValue, and.RightHandValue, (a, b) => a && b, parameterValues);
    }

    private FoldResult FoldOr(AnalysisContext context, Or or, IReadOnlyDictionary<NodeId, object?>? parameterValues = null) {
        var leftValue = GetConstantValue(context, or.LeftHandValue, parameterValues);
        if (leftValue.HasValue && leftValue.Value is bool leftBool && leftBool) {
            return FoldResult.Success(true);
        }

        return FoldBinaryBoolean(context, or.LeftHandValue, or.RightHandValue, (a, b) => a || b, parameterValues);
    }

    private FoldResult FoldUnaryBoolean(AnalysisContext context, Node operand, Func<bool, bool> operation, IReadOnlyDictionary<NodeId, object?>? parameterValues = null) {
        var value = GetConstantValue(context, operand, parameterValues);
        if (!value.HasValue)
            return FoldResult.NotFoldable;

        if (value.Value is bool boolValue) {
            return FoldResult.Success(operation(boolValue));
        }

        return FoldResult.NotFoldable;
    }

    private Node? SimplifyAddition(AnalysisContext context, Add add) {
        if (IsZero(context, add.LeftHandValue)) {
            return add.RightHandValue;
        }

        if (IsZero(context, add.RightHandValue)) {
            return add.LeftHandValue;
        }

        return null;
    }

    private Node? SimplifySubtraction(AnalysisContext context, Subtract subtract) {
        return IsZero(context, subtract.RightHandValue) ? subtract.LeftHandValue : null;
    }

    private Node? SimplifyMultiplication(AnalysisContext context, Multiply multiply) {
        if (IsOne(context, multiply.LeftHandValue))
            return multiply.RightHandValue;
        if (IsOne(context, multiply.RightHandValue))
            return multiply.LeftHandValue;
        if (IsZero(context, multiply.LeftHandValue) || IsZero(context, multiply.RightHandValue))
            return new Constant(0L);
        // x * 2^n → shift left by n
        var mulRightVal = GetConstantValue(context, multiply.RightHandValue);
        if (mulRightVal.HasValue && ToLong(mulRightVal.Value) is long mr && IsPowerOf2(mr))
            return new ShiftLeft(multiply.LeftHandValue, new Constant((int)double.Log2(mr)));
        return null;
    }

    private Node? SimplifyDivision(AnalysisContext context, Divide divide) {
        if (IsOne(context, divide.RightHandValue))
            return divide.LeftHandValue;
        // x / 2^n → shift right by n
        var divRightVal = GetConstantValue(context, divide.RightHandValue);
        if (divRightVal.HasValue && ToLong(divRightVal.Value) is long dr && IsPowerOf2(dr))
            return new ShiftRight(divide.LeftHandValue, new Constant((int)double.Log2(dr)));
        return null;
    }

    private Node? SimplifyModulo(AnalysisContext context, Modulo mod) {
        // x % 2^n → bitwise and with (2^n - 1)
        var modRightVal = GetConstantValue(context, mod.RightHandValue);
        if (modRightVal.HasValue && ToLong(modRightVal.Value) is long mr && IsPowerOf2(mr))
            return new BitwiseAnd(mod.LeftHandValue, new Constant(mr - 1));
        return null;
    }

    private Node? SimplifyUnaryMinus(AnalysisContext context, UnaryMinus neg) {
        // -(-x) → x
        return neg.Operand is UnaryMinus inner ? inner.Operand : null;
    }

    private Node? SimplifyBitwiseNot(AnalysisContext context, BitwiseNot bn) {
        // ~~x → x
        return bn.Operand is BitwiseNot inner ? inner.Operand : null;
    }

    private Node? SimplifyNot(AnalysisContext context, Not n) {
        // !!x → x
        return n.Value is Not inner ? inner.Value : null;
    }

    private Node? SimplifyBitwiseAnd(AnalysisContext context, BitwiseAnd ba) {
        if (IsZero(context, ba.RightHandValue))
            return new Constant(0L);
        var baVal = GetConstantValue(context, ba.RightHandValue);
        if (baVal.HasValue && ToLong(baVal.Value) == -1)
            return ba.LeftHandValue;
        return null;
    }

    private Node? SimplifyBitwiseOr(AnalysisContext context, BitwiseOr bo) {
        if (IsZero(context, bo.RightHandValue))
            return bo.LeftHandValue;
        var boVal = GetConstantValue(context, bo.RightHandValue);
        if (boVal.HasValue && ToLong(boVal.Value) == -1)
            return new Constant(-1L);
        return null;
    }

    private Node? SimplifyBitwiseXor(AnalysisContext context, BitwiseXor bx) {
        if (IsZero(context, bx.RightHandValue))
            return bx.LeftHandValue;
        return null;
    }

    /// <summary>int * int can overflow 32 bits; promote to long and
    /// return long when the result doesn't fit in int.</summary>
    private static object SafeIntMultiply(int x, int y) {
        long r = (long)x * y;
        if (r >= int.MinValue && r <= int.MaxValue)
            return (int)r;
        return r;
    }

    private static object NegateInt(int x) {
        if (x == int.MinValue)
            return -(long)x;
        return -x;
    }

    private static long? ToLong(object? value) => value switch {
        int i => i,
        long l => l,
        short s => s,
        byte b => b,
        uint u => u,
        _ => null
    };

    private Node? SimplifyAnd(AnalysisContext context, And and) {
        var leftValue = GetConstantValue(context, and.LeftHandValue);
        if (!leftValue.HasValue || leftValue.Value is not bool leftBool) {
            return null;
        }

        return leftBool ? and.RightHandValue : and.LeftHandValue;
    }

    private Node? SimplifyOr(AnalysisContext context, Or or) {
        var leftValue = GetConstantValue(context, or.LeftHandValue);
        if (!leftValue.HasValue || leftValue.Value is not bool leftBool) {
            return null;
        }

        return leftBool ? or.LeftHandValue : or.RightHandValue;
    }

    private FoldResult FoldComparison(AnalysisContext context, Node left, Node right, Func<object, object, bool> operation, IReadOnlyDictionary<NodeId, object?>? parameterValues = null) {
        var leftValue = GetConstantValue(context, left, parameterValues);
        var rightValue = GetConstantValue(context, right, parameterValues);

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

    private FoldResult FoldEquality(AnalysisContext context, Node left, Node right, Func<object?, object?, bool> operation, IReadOnlyDictionary<NodeId, object?>? parameterValues = null) {
        var leftValue = GetConstantValue(context, left, parameterValues);
        var rightValue = GetConstantValue(context, right, parameterValues);

        if (!leftValue.HasValue || !rightValue.HasValue)
            return FoldResult.NotFoldable;

        return FoldResult.Success(operation(leftValue.Value, rightValue.Value));
    }

    private FoldResult FoldConditional(AnalysisContext context, Conditional cond, IReadOnlyDictionary<NodeId, object?>? parameterValues = null) {
        var condValue = GetConstantValue(context, cond.Condition, parameterValues);
        if (!condValue.HasValue || condValue.Value is not bool boolCond)
            return FoldResult.NotFoldable;

        // If condition is constant, result is the appropriate branch
        var selectedBranch = boolCond ? cond.IfTrue : cond.IfFalse;
        var branchValue = GetConstantValue(context, selectedBranch, parameterValues);
        return branchValue.HasValue ? FoldResult.Success(branchValue.Value) : FoldResult.NotFoldable;
    }

    private FoldResult FoldIfStatement(AnalysisContext context, IfStatement ifStmt, IReadOnlyDictionary<NodeId, object?>? parameterValues = null) {
        var condValue = GetConstantValue(context, ifStmt.Condition, parameterValues);
        if (!condValue.HasValue || condValue.Value is not bool boolCond)
            return FoldResult.NotFoldable;

        // If condition is constant true, result is then branch
        // If condition is constant false, result is else branch (if present)
        if (boolCond) {
            var thenValue = GetConstantValue(context, ifStmt.ThenBranch, parameterValues);
            return thenValue.HasValue ? FoldResult.Success(thenValue.Value) : FoldResult.NotFoldable;
        }
        else if (ifStmt.ElseBranch != null) {
            var elseValue = GetConstantValue(context, ifStmt.ElseBranch, parameterValues);
            return elseValue.HasValue ? FoldResult.Success(elseValue.Value) : FoldResult.NotFoldable;
        }

        return FoldResult.NotFoldable;
    }

    private FoldResult FoldCoalesce(AnalysisContext context, Coalesce coalesce, IReadOnlyDictionary<NodeId, object?>? parameterValues = null) {
        var leftValue = GetConstantValue(context, coalesce.LeftHandValue, parameterValues);
        if (!leftValue.HasValue)
            return FoldResult.NotFoldable;

        // If left is not null, result is left
        if (leftValue.Value != null) {
            return FoldResult.Success(leftValue.Value);
        }

        // If left is null, result is right
        var rightValue = GetConstantValue(context, coalesce.RightHandValue, parameterValues);
        return rightValue.HasValue ? FoldResult.Success(rightValue.Value) : FoldResult.NotFoldable;
    }

    private FoldResult FoldInvocation(AnalysisContext context, Invoke invoke, IReadOnlyDictionary<NodeId, object?>? parameterValues = null) {
        if (invoke.Delegate is not Lambda lambda || lambda.Parameters.Count != invoke.Arguments.Length) {
            return FoldResult.NotFoldable;
        }

        Dictionary<NodeId, object?> boundParameters = parameterValues != null
            ? new Dictionary<NodeId, object?>(parameterValues)
            : [];

        for (var i = 0; i < lambda.Parameters.Count; i++) {
            var argumentValue = GetConstantValue(context, invoke.Arguments[i], parameterValues);
            if (!argumentValue.HasValue) {
                return FoldResult.NotFoldable;
            }

            boundParameters[lambda.Parameters[i].Id] = argumentValue.Value;
        }

        return GetConstantValue(context, lambda.Body, boundParameters);
    }

    private FoldResult GetConstantValue(AnalysisContext context, Node node, IReadOnlyDictionary<NodeId, object?>? parameterValues = null) {
        if (node is Parameter parameter) {
            return FoldParameter(parameter, parameterValues);
        }

        // Check if we already computed a constant value for this node
        var metadata = context.GetMetadata<ConstantValueMetadata>(node);
        if (metadata != null) {
            return FoldResult.Success(metadata.Value);
        }

        // Check if it's a literal constant
        if (node is Constant c) {
            return FoldResult.Success(c.Value);
        }

        return TryFold(context, node, parameterValues);
    }

    private bool IsZero(AnalysisContext context, Node node) {
        var value = GetConstantValue(context, node);
        return value.HasValue && value.Value switch {
            sbyte x => x == 0,
            byte x => x == 0,
            short x => x == 0,
            ushort x => x == 0,
            int x => x == 0,
            uint x => x == 0,
            long x => x == 0,
            ulong x => x == 0,
            float x => x == 0,
            double x => x == 0,
            decimal x => x == 0,
            _ => false
        };
    }

    private bool IsOne(AnalysisContext context, Node node) {
        var value = GetConstantValue(context, node);
        return value.HasValue && value.Value switch {
            sbyte x => x == 1,
            byte x => x == 1,
            short x => x == 1,
            ushort x => x == 1,
            int x => x == 1,
            uint x => x == 1,
            long x => x == 1,
            ulong x => x == 1,
            float x => x == 1,
            double x => x == 1,
            decimal x => x == 1,
            _ => false
        };
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
        (int x, int y) => SafeIntMultiply(x, y),
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
        // int.MinValue overflows int negation; cast to long first.
        // Use if/return to avoid ternary type-promoting int to long.
        int x => NegateInt(x),
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

    extension(INodeMetadataProvider context) {
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
}