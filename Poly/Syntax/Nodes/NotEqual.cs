using Poly.Interpretation.Analysis.Semantics;

namespace Poly.Syntax.Nodes;

/// <summary>
/// Represents an inequality comparison between two values.
/// </summary>
/// <remarks>
/// Compiles to <see cref="Expr.NotEqual"/> which tests if two values are not equal.
/// Corresponds to the <c>!=</c> operator in C#.
/// Type information is resolved by semantic analysis middleware.
/// </remarks>
public sealed record NotEqual(Node LeftHandValue, Node RightHandValue) : Expression {
    /// <inheritdoc />
    public override IEnumerable<Node?> Children => [LeftHandValue, RightHandValue];

    public override string ToString() => $"{LeftHandValue} != {RightHandValue}";

    /// <inheritdoc />
    public override IEnumerable<Poly.Syntax.Primitives.PrimitiveNode> ToPrimitives(Analysis.AnalysisContext context) {
        foreach (var p in LeftHandValue.ToPrimitives(context)) yield return p;
        foreach (var p in RightHandValue.ToPrimitives(context)) yield return p;

        // Check if comparing heap-backed reference types (string, etc).
        System.Type? eqType = null;
        if (context.GetResolvedType(LeftHandValue) is Poly.Introspection.CommonLanguageRuntime.IClrTypeDefinition clrL && !clrL.RuntimeType.IsValueType)
            eqType = clrL.RuntimeType;
        else if (context.GetResolvedType(RightHandValue) is Poly.Introspection.CommonLanguageRuntime.IClrTypeDefinition clrR && !clrR.RuntimeType.IsValueType)
            eqType = clrR.RuntimeType;

        if (eqType is null) {
            if ((LeftHandValue is Constant lc && lc.Value is string)
                || (RightHandValue is Constant rc && rc.Value is string))
                eqType = typeof(string);
        }

        yield return new Poly.Syntax.Primitives.BinaryOp(Poly.Syntax.Primitives.OpKind.Neq, eqType);
    }
}