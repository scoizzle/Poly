namespace Poly.DomainModeling;
/// <summary>
/// Shared full-tree <see cref="DomainExpression"/> rewrite base (coh-d1).
/// Composite nodes recurse into children through <see cref="DomainExpressionDispatch{TResult}.Route"/>
/// (single switch ownership for the hierarchy); leaf nodes default to identity.
/// Subclasses override only the leaves they transform — the composite
/// reconstruction no longer needs to be duplicated per concern.
///
/// <para>Fail-loud: <see cref="Default"/> throws if a concrete rewrite leaves a
/// new expression subtype unhandled (no silent pass-through).</para>
/// </summary>
public abstract class DomainExpressionRewriteBase : DomainExpressionDispatch<DomainExpression> {
    /// <summary>
    /// Catch-all for expression subtypes not overridden by a concrete rewrite.
    /// Fail-loud instead of silently returning the node unchanged.
    /// </summary>
    protected sealed override DomainExpression Default() =>
        throw new NotSupportedException(
            $"Expression subtype not handled by rewrite '{GetType().Name}'. " +
            "Override the subtype method or the base identity leaf.");

    // ── Leaf nodes — identity by default ─────────────────────────

    protected override DomainExpression PropertyAccess(PropertyAccess e) => e;
    protected override DomainExpression ParameterAccess(ParameterAccess e) => e;
    protected override DomainExpression Literal(Literal e) => e;

    // ── Composite nodes — recurse into children ──────────────────

    protected override DomainExpression And(And e) =>
        e with { Left = Route(e.Left), Right = Route(e.Right) };
    protected override DomainExpression Or(Or e) =>
        e with { Left = Route(e.Left), Right = Route(e.Right) };
    protected override DomainExpression Not(Not e) =>
        e with { Operand = Route(e.Operand) };
    protected override DomainExpression Comparison(Comparison e) =>
        e with { Left = Route(e.Left), Right = Route(e.Right) };
    protected override DomainExpression Add(Add e) =>
        e with { Left = Route(e.Left), Right = Route(e.Right) };
    protected override DomainExpression Subtract(Subtract e) =>
        e with { Left = Route(e.Left), Right = Route(e.Right) };
    protected override DomainExpression Multiply(Multiply e) =>
        e with { Left = Route(e.Left), Right = Route(e.Right) };
    protected override DomainExpression Divide(Divide e) =>
        e with { Left = Route(e.Left), Right = Route(e.Right) };
    protected override DomainExpression OwnedAccess(OwnedAccess e) =>
        e with { Inner = Route(e.Inner) };
    protected override DomainExpression Exists(Exists e) =>
        e with { Target = Route(e.Target) };
    protected override DomainExpression NotExists(NotExists e) =>
        e with { Target = Route(e.Target) };
    protected override DomainExpression AnyExpr(AnyExpr e) =>
        e with { Body = Route(e.Body) };
    protected override DomainExpression AllExpr(AllExpr e) =>
        e with { Body = Route(e.Body) };
    protected override DomainExpression NoneExpr(NoneExpr e) =>
        e with { Body = Route(e.Body) };
    protected override DomainExpression CountExpr(CountExpr e) =>
        e with { Body = e.Body is null ? null : Route(e.Body) };
    protected override DomainExpression RelationshipNavigation(RelationshipNavigation e) =>
        e with { TargetProperty = Route(e.TargetProperty) };
}