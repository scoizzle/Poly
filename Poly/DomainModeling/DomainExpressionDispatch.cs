namespace Poly.DomainModeling;

/// <summary>
/// Dispatch base for the <see cref="DomainExpression"/> hierarchy.
/// Subclasses override the methods for subtypes they handle.
/// Methods are named by the type they handle, not by the pattern.
/// The concern (lower, print, parse) lives in the subclass name.
/// </summary>
public abstract class DomainExpressionDispatch<TResult> {
    /// <summary>
    /// Default result for expression subtypes this concern does not handle.
    /// </summary>
    protected abstract TResult Default();

    // ── Methods named by the DomainExpression subtype ──

    protected virtual TResult PropertyAccess(PropertyAccess e) => Default();
    protected virtual TResult ParameterAccess(ParameterAccess e) => Default();
    protected virtual TResult Literal(Literal e) => Default();
    protected virtual TResult OwnedAccess(OwnedAccess e) => Default();
    protected virtual TResult Exists(Exists e) => Default();
    protected virtual TResult NotExists(NotExists e) => Default();
    protected virtual TResult Subtract(Subtract e) => Default();
    protected virtual TResult Add(Add e) => Default();
    protected virtual TResult Multiply(Multiply e) => Default();
    protected virtual TResult Divide(Divide e) => Default();
    protected virtual TResult DateOperation(DateOperation e) => Default();
    protected virtual TResult RelationshipNavigation(RelationshipNavigation e) => Default();
    protected virtual TResult AnyExpr(AnyExpr e) => Default();
    protected virtual TResult AllExpr(AllExpr e) => Default();
    protected virtual TResult NoneExpr(NoneExpr e) => Default();
    protected virtual TResult CountExpr(CountExpr e) => Default();
    protected virtual TResult Comparison(Comparison e) => Default();
    protected virtual TResult And(And e) => Default();
    protected virtual TResult Or(Or e) => Default();
    protected virtual TResult Not(Not e) => Default();

    /// <summary>
    /// Routes a <see cref="DomainExpression"/> to the appropriate handler method.
    /// New subtypes cause a compile error here if not added to the switch.
    /// </summary>
    public TResult Route(DomainExpression expr) => expr switch {
        PropertyAccess e => PropertyAccess(e),
        ParameterAccess e => ParameterAccess(e),
        Literal e => Literal(e),
        OwnedAccess e => OwnedAccess(e),
        Exists e => Exists(e),
        NotExists e => NotExists(e),
        Subtract e => Subtract(e),
        Add e => Add(e),
        Multiply e => Multiply(e),
        Divide e => Divide(e),
        DateOperation e => DateOperation(e),
        RelationshipNavigation e => RelationshipNavigation(e),
        AnyExpr e => AnyExpr(e),
        AllExpr e => AllExpr(e),
        NoneExpr e => NoneExpr(e),
        CountExpr e => CountExpr(e),
        Comparison e => Comparison(e),
        And e => And(e),
        Or e => Or(e),
        Not e => Not(e),
        _ => throw new NotSupportedException(
            $"Expression type '{expr.GetType().Name}' is not handled by {GetType().Name}")
    };
}