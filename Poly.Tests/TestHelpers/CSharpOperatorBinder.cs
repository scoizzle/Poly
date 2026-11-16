using Microsoft.CSharp.RuntimeBinder;

using Poly.Introspection;

namespace Poly.Tests.TestHelpers;

/// <summary>
/// C# as the oracle for an operator over runtime-typed operands: the C# runtime binder applies <paramref name="kind"/> to
/// the operand values exactly as compiled C# would to non-constant operands of their runtime types. The suite's one copy;
/// <see cref="CSharpOperatorOracle"/> is the Roslyn half, for the native integers the runtime binder does not model
/// (it applies IntPtr's declared members, not C# 11's operators).
/// </summary>
public static class CSharpOperatorBinder {
    /// <summary>
    /// C#'s value of the operator. Throws <see cref="RuntimeBinderException"/> when C# defines no operator for the operand
    /// types, and whatever C# throws when it evaluates (a zero divisor, an overflow in a checked context). <c>&amp;&amp;</c>
    /// and <c>||</c> short-circuit as in C#.
    /// </summary>
    public static object Apply(OperatorKind kind, object left, object? right) {
        dynamic x = left;
        dynamic? y = right;
        return kind switch {
            OperatorKind.Add => x + y,
            OperatorKind.Subtract => x - y,
            OperatorKind.Multiply => x * y,
            OperatorKind.Divide => x / y,
            OperatorKind.Modulo => x % y,
            OperatorKind.Equal => x == y,
            OperatorKind.NotEqual => x != y,
            OperatorKind.LessThan => x < y,
            OperatorKind.LessThanOrEqual => x <= y,
            OperatorKind.GreaterThan => x > y,
            OperatorKind.GreaterThanOrEqual => x >= y,
            OperatorKind.And => x && y,
            OperatorKind.Or => x || y,
            OperatorKind.BitwiseAnd => x & y,
            OperatorKind.BitwiseOr => x | y,
            OperatorKind.BitwiseXor => x ^ y,
            OperatorKind.ShiftLeft => x << y,
            OperatorKind.ShiftRight => x >> y,
            OperatorKind.Not => !x,
            OperatorKind.UnaryMinus => -x,
            OperatorKind.UnaryPlus => +x,
            OperatorKind.BitwiseNot => ~x,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };
    }

    /// <summary>The type of C#'s result for the operand values, or null when C# defines no operator for their types.</summary>
    public static Type? ResultType(OperatorKind kind, object left, object? right) {
        try {
            return Apply(kind, left, right).GetType();
        }
        catch (RuntimeBinderException) {
            return null;
        }
    }
}