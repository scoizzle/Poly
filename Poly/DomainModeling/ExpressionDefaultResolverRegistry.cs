using Poly.Ast.Nodes;

namespace Poly.DomainModeling;

/// <summary>
/// Resolves a pack-owned default expression (<c>Now</c>/<c>today</c> from the temporal pack)
/// to its CLR runtime value and export node, adapted to the target property's CLR type when
/// known. The pack registers one resolver per expression type so the core runtime
/// (<c>DomainEntityInstance.EvaluateDefaultValue</c>) and export
/// (<c>EffectLoweringPass.LowerDefaultExpression</c>) never name pack IR.
/// </summary>
public interface IExpressionDefaultResolver {
    /// <summary>The concrete expression type this resolver owns.</summary>
    Type ExpressionType { get; }

    /// <summary>
    /// Resolves <paramref name="expression"/> (of <see cref="ExpressionType"/>) to a runtime
    /// value and an export node, adapted to <paramref name="propTypeName"/> (e.g.
    /// <c>DateTime</c>/<c>Timestamp</c> vs <c>Date</c>). Return false when not applicable.
    /// </summary>
    bool TryResolve(DomainExpression expression, string? propTypeName, out object? runtimeValue, out Node exportNode);
}

/// <summary>
/// Ambient registry of pack-owned default-expression resolvers. <see cref="Default"/> is the
/// product-default set the built-in temporal pack contributes to.
/// </summary>
public sealed class ExpressionDefaultResolverRegistry {
    private readonly List<IExpressionDefaultResolver> _resolvers = [];

    /// <summary>Ambient product-default resolver set (built-in packs register here).</summary>
    public static ExpressionDefaultResolverRegistry Default { get; } = new();

    public void Register(IExpressionDefaultResolver resolver) {
        ArgumentNullException.ThrowIfNull(resolver);
        if (_resolvers.Any(r => r.ExpressionType == resolver.ExpressionType)) {
            throw new InvalidOperationException(
                $"Duplicate expression default resolver for '{resolver.ExpressionType.Name}'.");
        }
        _resolvers.Add(resolver);
    }

    /// <summary>Runs the registered resolver for <paramref name="expression"/>, if any.</summary>
    public bool TryResolve(DomainExpression expression, string? propTypeName, out object? runtimeValue, out Node exportNode) {
        ArgumentNullException.ThrowIfNull(expression);
        foreach (var resolver in _resolvers) {
            if (resolver.ExpressionType.IsInstanceOfType(expression)
                && resolver.TryResolve(expression, propTypeName, out runtimeValue!, out exportNode!))
                return true;
        }
        runtimeValue = null;
        exportNode = null!;
        return false;
    }
}