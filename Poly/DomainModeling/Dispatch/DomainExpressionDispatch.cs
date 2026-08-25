using Poly.DomainModeling.Ontology;

using Add = Poly.DomainModeling.Ontology.Add;
using And = Poly.DomainModeling.Ontology.And;
using Divide = Poly.DomainModeling.Ontology.Divide;
using Multiply = Poly.DomainModeling.Ontology.Multiply;
using Not = Poly.DomainModeling.Ontology.Not;
using Or = Poly.DomainModeling.Ontology.Or;
using Subtract = Poly.DomainModeling.Ontology.Subtract;

namespace Poly.DomainModeling.Dispatch;

/// <summary>
/// Dispatch base for the <see cref="DomainExpression"/> hierarchy.
/// Subclasses override the methods for core subtypes they handle.
/// Methods are named by the type they handle, not by the pattern.
/// The concern (lower, print, parse) lives in the subclass name.
///
/// <para>Core subtypes route through the closed switch below, including temporal
/// clocks and durations (same assembly). Unhandled subtypes hit <see cref="Default"/>.</para>
/// </summary>
public abstract class DomainExpressionDispatch<TResult> {

    /// <summary>
    /// Default result for expression subtypes this concern does not handle.
    /// </summary>
    protected abstract TResult Default();

    // ── Methods named by the core DomainExpression subtype ──

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
    protected virtual TResult RelationshipNavigation(RelationshipNavigation e) => Default();
    protected virtual TResult AnyExpr(AnyExpr e) => Default();
    protected virtual TResult AllExpr(AllExpr e) => Default();
    protected virtual TResult NoneExpr(NoneExpr e) => Default();
    protected virtual TResult CountExpr(CountExpr e) => Default();
    protected virtual TResult Comparison(Comparison e) => Default();
    protected virtual TResult And(And e) => Default();
    protected virtual TResult Or(Or e) => Default();
    protected virtual TResult Not(Not e) => Default();
    protected virtual TResult Now(Now e) => Default();
    protected virtual TResult Today(Today e) => Default();
    protected virtual TResult Duration(Duration e) => Default();
    protected virtual TResult DateOperation(DateOperation e) => Default();

    /// <summary>
    /// Routes a <see cref="DomainExpression"/> to the appropriate handler method.
    /// Core subtypes use the switch; pack-owned subtypes fall through to the
    /// registered handler registry. New core subtypes cause a compile error here
    /// if not added to the switch.
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
        RelationshipNavigation e => RelationshipNavigation(e),
        AnyExpr e => AnyExpr(e),
        AllExpr e => AllExpr(e),
        NoneExpr e => NoneExpr(e),
        CountExpr e => CountExpr(e),
        Comparison e => Comparison(e),
        And e => And(e),
        Or e => Or(e),
        Not e => Not(e),
        Now e => Now(e),
        Today e => Today(e),
        Duration e => Duration(e),
        DateOperation e => DateOperation(e),
        _ => Default(),
    };
}