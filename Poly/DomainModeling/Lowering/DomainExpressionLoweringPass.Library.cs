using System.Diagnostics.CodeAnalysis;

using Poly.Ast.Nodes;
using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Lowering;

// Library: pack-owned expression subtypes, lowered through the session Meaning handlers.

public sealed partial class DomainExpressionLoweringPass {
    protected override LoweredExpression Library(DomainExpression expr) {
        if (TryLowerLibrary(expr, out var lowered))
            return lowered;
        throw new NotSupportedException(
            $"DomainExpression node type '{expr.GetType().Name}' is not supported");
    }

    /// <summary>
    /// Library handlers receive a value-only route. This records each child's
    /// statements and combines them with the handler's node.
    /// </summary>
    private bool TryLowerLibrary(DomainExpression expr, [NotNullWhen(true)] out LoweredExpression? lowered) {
        var children = new List<LoweredExpression>();
        Node RouteChild(DomainExpression child) {
            var routed = Route(child);
            children.Add(routed);
            return routed.Value;
        }
        if (EffectiveMeaning.Lowering.TryLower(expr, RouteChild, _propertyTypeResolver, out var node)) {
            lowered = LoweredExpression.Combine(children, node);
            return true;
        }
        lowered = null;
        return false;
    }
}