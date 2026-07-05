using Poly.Interpretation.Analysis.Semantics;

namespace Poly.Syntax.Nodes;

/// <summary>
/// Represents an equality comparison between two values.
/// </summary>
/// <remarks>
/// Compiles to <see cref="Expr.Equal"/> which tests if two values are equal.
/// Corresponds to the <c>==</c> operator in C#.
/// Type information is resolved by semantic analysis middleware.
/// </remarks>
public sealed record Equal(Node LeftHandValue, Node RightHandValue) : Expression {
    /// <inheritdoc />
    public override IEnumerable<Node?> Children => [LeftHandValue, RightHandValue];

    /// <inheritdoc />
    public override string ToString() => $"{LeftHandValue} == {RightHandValue}";

    /// <inheritdoc />
    public override IEnumerable<Primitives.PrimitiveNode> ToPrimitives(Primitives.ExpansionContext context) {
        foreach (var p in LeftHandValue.ToPrimitives(context)) yield return p;
        foreach (var p in RightHandValue.ToPrimitives(context)) yield return p;

        // Check if comparing heap-backed reference types (string, etc).
        Type? eqType = null;
        if (context.Analysis.GetResolvedType(LeftHandValue) is Introspection.CommonLanguageRuntime.IClrTypeDefinition clrL && !clrL.RuntimeType.IsValueType)
            eqType = clrL.RuntimeType;
        else if (context.Analysis.GetResolvedType(RightHandValue) is Introspection.CommonLanguageRuntime.IClrTypeDefinition clrR && !clrR.RuntimeType.IsValueType)
            eqType = clrR.RuntimeType;

        if (eqType is null) {
            if ((LeftHandValue is Constant lc && lc.Value is string)
                || (RightHandValue is Constant rc && rc.Value is string))
                eqType = typeof(string);
        }

        yield return new Primitives.BinaryOp(Poly.Syntax.Primitives.OpKind.Eq, eqType);
    }
}