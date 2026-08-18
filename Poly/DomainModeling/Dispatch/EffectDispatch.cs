using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Dispatch;

/// <summary>
/// Dispatch base for the <see cref="Effect"/> hierarchy.
/// Subclasses override the methods for subtypes they handle.
/// Methods are named by the type they handle, not by the pattern.
/// The concern (lower, print, execute) lives in the subclass name.
/// </summary>
public abstract class EffectDispatch<TResult> {
    /// <summary>
    /// Default result for Effect subtypes this concern does not handle.
    /// </summary>
    protected abstract TResult Default();

    // ── Methods named by the Effect subtype ──

    protected virtual TResult StageTransition(StageTransitionEffect e) => Default();
    protected virtual TResult Assign(AssignEffect e) => Default();
    protected virtual TResult CreateEntityInstance(CreateEntityInstance e) => Default();
    protected virtual TResult CreateEntityInRelationship(CreateEntityInRelationshipEffect e) => Default();
    protected virtual TResult InvokeAction(InvokeActionEffect e) => Default();
    protected virtual TResult ForEachInvoke(ForEachInvokeEffect e) => Default();
    protected virtual TResult Composite(CompositeEffect e) => Default();
    protected virtual TResult Conditional(ConditionalEffect e) => Default();

    /// <summary>
    /// Routes an <see cref="Effect"/> to the appropriate handler method.
    /// New subtypes cause a compile error here if not added to the switch.
    /// </summary>
    public TResult Route(Effect effect) => effect switch {
        StageTransitionEffect e => StageTransition(e),
        AssignEffect e => Assign(e),
        CreateEntityInstance e => CreateEntityInstance(e),
        CreateEntityInRelationshipEffect e => CreateEntityInRelationship(e),
        InvokeActionEffect e => InvokeAction(e),
        ForEachInvokeEffect e => ForEachInvoke(e),
        CompositeEffect e => Composite(e),
        ConditionalEffect e => Conditional(e),
        _ => throw new NotSupportedException(
            $"Effect type '{effect.GetType().Name}' is not handled by {GetType().Name}")
    };
}