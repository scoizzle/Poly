using System.Linq.Expressions;

using static System.Linq.Expressions.Expression;

namespace Poly.Interpretation.Vm.Abi;

public static partial class AbiEmitter {
    private enum CompareShape { Null, Optional, Enum, Inline, HeapValue, Reference }

    /// <summary>The value a pair is promoted to. Integral pairs use <c>decimal</c>
    /// because it holds every <c>ulong</c>, <c>char</c>, and signed 64-bit value
    /// exactly, so there is no <c>long</c> domain and no <c>Convert(ulong, long)</c>.</summary>
    private enum CompareDomain { Boolean, Double, Float, Decimal, Value, Reference }

    private readonly record struct CompareOperand(CompareShape Shape, Type? Clr, Type? Underlying);

    /// <summary>Comparison as a bool. Operands are emitted left then right and each
    /// is read once. The domain is C# binary numeric promotion plus the nullable
    /// lift, and the constant fold uses the same promotion: an integral operand
    /// promotes to <c>float</c> when the other side is <c>float</c>. A
    /// <c>decimal</c> against <c>float</c> or <c>double</c> compares in
    /// <c>double</c>, which is total and loses precision above 2^53.</summary>
    private static Expression CompileCompareTest(
        Node left, Node right,
        Func<Expression, Expression, BinaryExpression> cf,
        Context ctx) {
        bool equality = cf == Equal || cf == NotEqual;
        var leftOp = CompareOperandOf(left, ctx);
        var rightOp = CompareOperandOf(right, ctx);
        if (leftOp.Shape == CompareShape.Null || rightOp.Shape == CompareShape.Null) {
            if (!equality)
                throw new InvalidOperationException(
                    "VM compile rejected: relational comparison with null is not supported.");
            return FoldNullComparison(left, right, leftOp, rightOp, cf == Equal, ctx);
        }

        var domain = CompareDomainOf(leftOp, rightOp);
        if (!equality && domain == CompareDomain.Boolean)
            throw new InvalidOperationException(
                "VM compile rejected: ordering comparison of 'Boolean' is not supported.");

        var leftRaw = ValueOf(left, ctx);
        var rightRaw = ValueOf(right, ctx);
        var l = Variable(leftRaw.Type, "_cmpL");
        var r = Variable(rightRaw.Type, "_cmpR");
        var test = CompareValues(l, r, leftOp, rightOp, domain, equality, cf, ctx);
        Expression answer = equality && cf != Equal ? Not(test) : test;
        return Block([l, r], Assign(l, leftRaw), Assign(r, rightRaw), answer);
    }

    /// <summary>Comparison as a ring value: the bool test, materialised as 0/1.</summary>
    private static Expression EmitComparisonValue(
        Node left, Node right,
        Func<Expression, Expression, BinaryExpression> cf,
        Context ctx) =>
        Condition(CompileCompareTest(left, right, cf, ctx), Constant(1L), Constant(0L));

    /// <summary>Comparison in ring form: the value form, spilled.</summary>
    private static Expression EmitComparison(
        Node left, Node right,
        Func<Expression, Expression, BinaryExpression> cf,
        Context ctx) =>
        SpillToRing(EmitComparisonValue(left, right, cf, ctx), ctx);

    /// <summary>Shape from the slot the writers produce. A null constant is matched
    /// before the null-CLR failure: <c>Classify</c> gives it <c>HeapRef</c> and no
    /// CLR type. An enum's slot is its underlying integer
    /// (<see cref="RingCodec.EnumWord"/>), whatever <c>Kind</c> would say about a
    /// box. A void call is an inline <c>long</c>:
    /// <c>EmitResult</c> writes <c>0L</c> into the slot it reserved. A statement
    /// with no CLR type still fails closed above that row.</summary>
    private static CompareOperand CompareOperandOf(Node node, Context ctx) {
        var effective = ValueRepresentationClassifier.Effective(ctx.Analysis, node);
        if (effective is Constant { Value: null })
            return new CompareOperand(CompareShape.Null, null, null);

        var (kind, clr) = ctx.RepresentationOf(node);
        if (kind == ValueRepresentationKind.Unknown || clr is null)
            throw new InvalidOperationException(
                $"VM compile rejected: cannot compare '{node.GetType().Name}' because its type did not resolve.");
        if (kind == ValueRepresentationKind.Void)
            return new CompareOperand(CompareShape.Inline, typeof(long), null);
        if (Nullable.GetUnderlyingType(clr) is { } underlying)
            return new CompareOperand(CompareShape.Optional, clr, underlying);
        if (clr.IsEnum)
            return new CompareOperand(CompareShape.Enum, clr, null);
        if (kind == ValueRepresentationKind.StackScalar)
            return new CompareOperand(CompareShape.Inline, clr, null);
        if (kind == ValueRepresentationKind.HeapRef && clr.IsValueType)
            return new CompareOperand(CompareShape.HeapValue, clr, null);
        if (kind == ValueRepresentationKind.HeapRef)
            return new CompareOperand(CompareShape.Reference, clr, null);
        throw new InvalidOperationException(
            $"VM compile rejected: cannot compare '{node.GetType().Name}' because its type did not resolve.");
    }

    /// <summary>First match wins. A <c>decimal</c> against <c>float</c> or <c>double</c>
    /// is <c>double</c> (the conversion never throws; <c>double</c> to <c>decimal</c>
    /// does, for NaN, infinity, and out of range). <c>float</c> is its own domain so
    /// an integral operand rounds the way C# does (<c>16777217u == 16777216f</c>).
    /// Ordering of <c>bool</c> fails closed: C# has no such operator and analysis
    /// still admits it. Mixed <c>ulong</c> and a signed value, which C# also has no
    /// operator for, compare in <c>decimal</c>.</summary>
    private static CompareDomain CompareDomainOf(CompareOperand left, CompareOperand right) {
        var l = DecodedType(left);
        var r = DecodedType(right);
        if (l == typeof(bool) && r == typeof(bool))
            return CompareDomain.Boolean;
        if (l.IsEnum || r.IsEnum)
            return CompareDomain.Value;
        bool leftFloat = l == typeof(float) || l == typeof(double);
        bool rightFloat = r == typeof(float) || r == typeof(double);
        if ((l == typeof(decimal) && rightFloat) || (r == typeof(decimal) && leftFloat))
            return CompareDomain.Double;
        if (l == typeof(double) || r == typeof(double))
            return CompareDomain.Double;
        if (l == typeof(float) || r == typeof(float))
            return CompareDomain.Float;
        if (l == typeof(decimal) || r == typeof(decimal))
            return CompareDomain.Decimal;
        if (IsIntegralOrChar(l) && IsIntegralOrChar(r))
            return CompareDomain.Decimal;
        if (l == r && l.IsValueType)
            return CompareDomain.Value;
        if (!l.IsValueType && !r.IsValueType)
            return CompareDomain.Reference;
        throw new InvalidOperationException(
            $"VM compile rejected: comparison between '{l.Name}' and '{r.Name}' has no common value.");
    }

    private static Type DecodedType(CompareOperand operand) =>
        (operand.Shape == CompareShape.Optional ? operand.Underlying : operand.Clr)
        ?? throw new InvalidOperationException(
            "VM compile rejected: cannot compare an operand because its type did not resolve.");

    private static bool IsIntegralOrChar(Type type) =>
        !type.IsEnum
        && (type == typeof(char)
            || Type.GetTypeCode(type) is TypeCode.SByte or TypeCode.Byte
                or TypeCode.Int16 or TypeCode.UInt16 or TypeCode.Int32
                or TypeCode.UInt32 or TypeCode.Int64 or TypeCode.UInt64);

    /// <summary>A null constant against a value. An optional or a reference uses the
    /// null sentinel. Every other value folds: <c>== null</c> is false and
    /// <c>!= null</c> is true, and the value operand still runs.</summary>
    private static Expression FoldNullComparison(
        Node left, Node right,
        CompareOperand leftOp, CompareOperand rightOp,
        bool equal, Context ctx) {
        if (leftOp.Shape == CompareShape.Null && rightOp.Shape == CompareShape.Null)
            return Constant(equal);

        bool nullOnLeft = leftOp.Shape == CompareShape.Null;
        var other = nullOnLeft ? right : left;
        var otherOp = nullOnLeft ? rightOp : leftOp;
        if (otherOp.Shape is CompareShape.Inline or CompareShape.HeapValue or CompareShape.Enum)
            return Block(ValueOf(other, ctx), Constant(!equal));

        var raw = ValueOf(other, ctx);
        var local = Variable(raw.Type, "_cmpNull");
        Expression test = Nullable.GetUnderlyingType(raw.Type) is not null
            ? equal
                ? Expression.Equal(local, Default(raw.Type))
                : Expression.NotEqual(local, Default(raw.Type))
            : equal
                ? Equal(RingCodec.ToWord(local), Constant(0L))
                : NotEqual(RingCodec.ToWord(local), Constant(0L));
        return Block([local], Assign(local, raw), test);
    }

    private static Expression CompareValues(
        Expression leftLocal, Expression rightLocal,
        CompareOperand leftOp, CompareOperand rightOp,
        CompareDomain domain, bool equality,
        Func<Expression, Expression, BinaryExpression> cf,
        Context ctx) {
        var (leftValue, _) = ReadCompareOperand(leftLocal, leftOp, ctx);
        var (rightValue, _) = ReadCompareOperand(rightLocal, rightOp, ctx);
        bool lift = Nullable.GetUnderlyingType(leftValue.Type) is not null
            || Nullable.GetUnderlyingType(rightValue.Type) is not null;
        if (lift)
            return LiftedCompare(leftValue, rightValue, domain, equality, cf);
        var left = ToDomain(leftValue, domain);
        var right = ToDomain(rightValue, domain);
        return domain is CompareDomain.Value or CompareDomain.Reference
            ? equality
                ? Call(ObjectEquals, left, right)
                : cf(Call(VmHeapCompare, left, right), Constant(0))
            : equality
                ? Expression.Equal(left, right)
                : cf(left, right);
    }

    /// <summary>The lifted CLR operator. Both sides are <c>Nullable&lt;T&gt;</c> of the
    /// domain, so an absent operand compares the way C# does and the body does not
    /// rebuild that rule.</summary>
    private static Expression LiftedCompare(
        Expression left, Expression right, CompareDomain domain, bool equality,
        Func<Expression, Expression, BinaryExpression> cf) {
        if (domain is CompareDomain.Value) {
            left = AsSameNullable(left, right);
            right = AsSameNullable(right, left);
            return equality ? Expression.Equal(left, right) : cf(left, right);
        }
        left = ToNullableDomain(left, domain);
        right = ToNullableDomain(right, domain);
        return equality ? Expression.Equal(left, right) : cf(left, right);
    }

    private static Expression AsSameNullable(Expression value, Expression other) {
        if (Nullable.GetUnderlyingType(value.Type) is not null)
            return value;
        if (Nullable.GetUnderlyingType(other.Type) is { } underlying && value.Type == underlying)
            return Convert(value, other.Type);
        return value;
    }

    private static Expression ToNullableDomain(Expression value, CompareDomain domain) {
        Type target = domain switch {
            CompareDomain.Double => typeof(double),
            CompareDomain.Float => typeof(float),
            CompareDomain.Decimal => typeof(decimal),
            CompareDomain.Boolean => typeof(bool),
            _ => Nullable.GetUnderlyingType(value.Type) ?? value.Type,
        };
        var nullable = typeof(Nullable<>).MakeGenericType(target);
        if (value.Type == nullable)
            return value;
        if (Nullable.GetUnderlyingType(value.Type) is not null)
            return Convert(value, nullable);
        Expression core = value.Type == target ? value : Convert(value, target);
        return Convert(core, nullable);
    }

    /// <summary>One CLR value for the slot. An inline word goes through
    /// <see cref="RingCodec.ScalarValue"/> (<c>ulong</c> is an unchecked bitcast,
    /// IEEE uses <see cref="RingCodec.FromIeee"/>, <c>bool</c> is <c>!= 0</c>).
    /// A typed slot that already holds the CLR type skips that. An enum word is
    /// that underlying integer converted back to the enum. A required heap value
    /// is <see cref="RingCodec.RequiredValue"/>; handle 0 fails closed. An
    /// optional is the Nullable&lt;T&gt; local, or the frame handle unboxed once.</summary>
    private static (Expression Value, Expression? Present) ReadCompareOperand(
        Expression local, CompareOperand operand, Context ctx) {
        switch (operand.Shape) {
            case CompareShape.Inline: {
                    var clr = operand.Clr!;
                    Expression value = local.Type == clr
                        ? local
                        : RingCodec.ScalarValue(RingCodec.ToWord(local), clr);
                    return (value, null);
                }
            case CompareShape.Enum: {
                    var clr = operand.Clr!;
                    var underlying = Enum.GetUnderlyingType(clr);
                    Expression word = local.Type == underlying
                        ? local
                        : RingCodec.ScalarValue(RingCodec.ToWord(local), underlying);
                    return (Convert(word, clr), null);
                }
            case CompareShape.HeapValue: {
                    var clr = operand.Clr!;
                    var boxed = Call(
                        RequiredValueInfo, ctx.HeapLocal, RingCodec.ToWord(local), Constant(clr, typeof(Type)));
                    return (Convert(boxed, clr), null);
                }
            case CompareShape.Optional: {
                    var nullable = operand.Clr!;
                    Expression value = local.Type == nullable
                        ? local
                        : local.Type == operand.Underlying
                            ? Convert(local, nullable)
                            : RingCodec.UnboxToNullable(local, nullable, ctx.HeapLocal);
                    return (value, null);
                }
            case CompareShape.Reference: {
                    var handle = RingCodec.ToWord(local);
                    Expression value = Condition(
                        Equal(handle, Constant(0L)),
                        Constant(null, typeof(object)),
                        RingCodec.DerefObject(ctx.HeapLocal, handle));
                    return (value, null);
                }
            default:
                throw new InvalidOperationException(
                    "VM compile rejected: cannot compare an operand because its type did not resolve.");
        }
    }

    private static Expression ToDomain(Expression value, CompareDomain domain) {
        Type target = domain switch {
            CompareDomain.Double => typeof(double),
            CompareDomain.Float => typeof(float),
            CompareDomain.Decimal => typeof(decimal),
            CompareDomain.Value or CompareDomain.Reference => typeof(object),
            _ => value.Type,
        };
        return value.Type == target ? value : Convert(value, target);
    }
}