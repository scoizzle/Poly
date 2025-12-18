using Poly.Introspection;

namespace Poly.Interpretation.Operators.Arithmetic;

/// <summary>
/// Provides utilities for numeric type promotion according to C# arithmetic rules.
/// </summary>
internal static class NumericTypePromotion {
    /// <summary>
    /// Determines the result type of a binary arithmetic operation between two numeric types.
    /// </summary>
    /// <param name="context">The interpretation context for type lookups.</param>
    /// <param name="leftType">The type of the left operand.</param>
    /// <param name="rightType">The type of the right operand.</param>
    /// <returns>The promoted type that will be the result of the operation.</returns>
    /// <remarks>
    /// This implements C# numeric promotion rules:
    /// - If either operand is decimal, the result is decimal
    /// - If either operand is double, the result is double
    /// - If either operand is float, the result is float
    /// - If either operand is ulong, the result is ulong
    /// - If either operand is long, the result is long
    /// - If either operand is uint, the result is uint
    /// - Otherwise, the result is int
    /// </remarks>
    public static ITypeDefinition GetPromotedType(
        InterpretationContext context,
        ITypeDefinition leftType,
        ITypeDefinition rightType) {

        var leftClrType = leftType.ReflectedType;
        var rightClrType = rightType.ReflectedType;

        // Handle nullable types by unwrapping to underlying type
        var leftUnderlyingType = Nullable.GetUnderlyingType(leftClrType) ?? leftClrType;
        var rightUnderlyingType = Nullable.GetUnderlyingType(rightClrType) ?? rightClrType;

        // Decimal has highest precedence
        if (leftUnderlyingType == typeof(decimal) || rightUnderlyingType == typeof(decimal)) {
            return context.GetTypeDefinition<decimal>()!;
        }

        // Double
        if (leftUnderlyingType == typeof(double) || rightUnderlyingType == typeof(double)) {
            return context.GetTypeDefinition<double>()!;
        }

        // Float
        if (leftUnderlyingType == typeof(float) || rightUnderlyingType == typeof(float)) {
            return context.GetTypeDefinition<float>()!;
        }

        // ULong
        if (leftUnderlyingType == typeof(ulong) || rightUnderlyingType == typeof(ulong)) {
            return context.GetTypeDefinition<ulong>()!;
        }

        // Long
        if (leftUnderlyingType == typeof(long) || rightUnderlyingType == typeof(long)) {
            return context.GetTypeDefinition<long>()!;
        }

        // UInt
        if (leftUnderlyingType == typeof(uint) || rightUnderlyingType == typeof(uint)) {
            return context.GetTypeDefinition<uint>()!;
        }

        // Default to int (includes byte, sbyte, short, ushort, int)
        return context.GetTypeDefinition<int>()!;
    }

    /// <summary>
    /// Converts expressions to a common promoted type for binary operations.
    /// </summary>
    /// <param name="context">The interpretation context.</param>
    /// <param name="leftExpr">The left operand expression.</param>
    /// <param name="rightExpr">The right operand expression.</param>
    /// <param name="leftType">The type definition of the left operand.</param>
    /// <param name="rightType">The type definition of the right operand.</param>
    /// <returns>A tuple of converted expressions at the promoted type.</returns>
    public static (Expression Left, Expression Right) ConvertToPromotedType(
        InterpretationContext context,
        Expression leftExpr,
        Expression rightExpr,
        ITypeDefinition leftType,
        ITypeDefinition rightType) {

        var promotedType = GetPromotedType(context, leftType, rightType);
        var promotedClrType = promotedType.ReflectedType;

        // Convert both expressions to the promoted type if needed
        var convertedLeft = leftExpr.Type == promotedClrType
            ? leftExpr
            : Expression.Convert(leftExpr, promotedClrType);

        var convertedRight = rightExpr.Type == promotedClrType
            ? rightExpr
            : Expression.Convert(rightExpr, promotedClrType);

        return (convertedLeft, convertedRight);
    }
}