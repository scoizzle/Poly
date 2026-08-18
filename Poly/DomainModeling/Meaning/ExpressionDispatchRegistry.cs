using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Meaning;

/// <summary>
/// Handles a pack-owned <see cref="DomainExpression"/> subtype for one core concern
/// (rewrite, lowering, analysis inference). Registered into an
/// <see cref="ExpressionDispatchRegistry{TResult}"/>; core dispatch consults the
/// session registry after its closed switch so packs extend the expression surface without
/// core naming pack types.
/// </summary>
/// <typeparam name="TResult">The concern's result type (e.g. <c>DomainExpression</c>,
/// <c>Syntax.Node</c>, <c>TypeCategory</c>).</typeparam>
public interface IExpressionDispatchHandler<TResult> {
    /// <summary>The concrete expression type this handler owns.</summary>
    Type ExpressionType { get; }

    /// <summary>
    /// Handles <paramref name="expression"/> (of <see cref="ExpressionType"/>).
    /// <paramref name="route"/> recurses into a child expression through the owning
    /// concern (respecting its subject/context), so handlers can rebuild composites.
    /// Return false to defer to the next handler / fail closed.
    /// </summary>
    bool TryHandle(DomainExpression expression, Func<DomainExpression, TResult> route, out TResult result);
}

/// <summary>
/// Ordered registry of pack-provided expression dispatch handlers on a session.
/// Duplicate <see cref="IExpressionDispatchHandler{TResult}.ExpressionType"/>
/// registration fails closed.
/// </summary>
public sealed class ExpressionDispatchRegistry<TResult> {
    private readonly List<IExpressionDispatchHandler<TResult>> _handlers = [];

    /// <summary>Registered handlers, in order.</summary>
    public IReadOnlyList<IExpressionDispatchHandler<TResult>> Handlers => _handlers;

    public void Register(IExpressionDispatchHandler<TResult> handler) {
        ArgumentNullException.ThrowIfNull(handler);
        if (_handlers.Any(h => h.ExpressionType == handler.ExpressionType)) {
            throw new InvalidOperationException(
                $"Duplicate expression dispatch handler for '{handler.ExpressionType.Name}' " +
                $"in registry '{typeof(TResult).Name}'.");
        }
        _handlers.Add(handler);
    }

    /// <summary>
    /// Dispatches <paramref name="expression"/> to the first registered handler whose
    /// <see cref="IExpressionDispatchHandler{TResult}.ExpressionType"/> matches. Returns
    /// false when no handler owns the type (the caller fails closed).
    /// </summary>
    public bool TryDispatch(DomainExpression expression, Func<DomainExpression, TResult> route, out TResult result) {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(route);
        foreach (var handler in _handlers) {
            if (handler.ExpressionType.IsInstanceOfType(expression)
                && handler.TryHandle(expression, route, out result!))
                return true;
        }
        result = default!;
        return false;
    }
}