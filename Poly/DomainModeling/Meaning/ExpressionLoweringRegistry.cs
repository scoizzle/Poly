using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Meaning;

/// <summary>
/// Library-owned DomainExpression → Syntax lowering. Core dispatch does not name
/// pack IR; handlers receive the same child <paramref name="route"/> and optional
/// property-type resolver the core pass uses for date CLR family.
/// </summary>
public interface IExpressionLoweringHandler {
    Type ExpressionType { get; }

    bool TryLower(
        DomainExpression expression,
        Func<DomainExpression, Node> route,
        Func<string, string?>? propertyTypeResolver,
        out Node result);
}

/// <summary>Session registry of pack-owned expression lowering handlers.</summary>
public sealed class ExpressionLoweringRegistry {
    private readonly List<IExpressionLoweringHandler> _handlers = [];

    public IReadOnlyList<IExpressionLoweringHandler> Handlers => _handlers;

    public void Register(IExpressionLoweringHandler handler) {
        ArgumentNullException.ThrowIfNull(handler);
        if (_handlers.Any(h => h.ExpressionType == handler.ExpressionType)) {
            throw new InvalidOperationException(
                $"Duplicate expression lowering handler for '{handler.ExpressionType.Name}'.");
        }
        _handlers.Add(handler);
    }

    public bool TryLower(
        DomainExpression expression,
        Func<DomainExpression, Node> route,
        Func<string, string?>? propertyTypeResolver,
        out Node result) {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(route);
        foreach (var handler in _handlers) {
            if (handler.ExpressionType.IsInstanceOfType(expression)
                && handler.TryLower(expression, route, propertyTypeResolver, out result!))
                return true;
        }
        result = null!;
        return false;
    }
}