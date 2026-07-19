using Poly.DomainModeling.Effects;
using Poly.Syntax.Nodes;

namespace Poly.DomainModeling.Lowering;

/// <summary>
/// Lowers domain <see cref="Effect"/> types to Syntax AST nodes for VM
/// execution via <see cref="Interpreter.Compile"/>. Composes with
/// <see cref="DomainExpressionLoweringPass"/> for expression-heavy effects
/// like <see cref="AssignEffect"/> and <see cref="ConditionalEffect"/>.
///
/// <para>Some effects (<see cref="StageTransitionEffect"/>,
/// <see cref="CreateEntityInstance"/>, <see cref="InvokeActionEffect"/>)
/// execute directly on <see cref="DomainEntityInstance"/> rather than
/// through the VM — they produce <c>null</c> from <see cref="Route"/>
/// and are handled by the caller.</para>
/// </summary>
public sealed class EffectLoweringPass : EffectDispatch<Node?> {
    private readonly Entity _entity;
    private readonly DomainExpressionLoweringPass _expressionPass;

    public EffectLoweringPass(Entity entity, Node subject)
        : this(entity, new LoweringContext(subject)) { }

    public EffectLoweringPass(Entity entity, LoweringContext context) {
        _entity = entity;
        _expressionPass = new DomainExpressionLoweringPass(context);
        Subject = context.Subject;
    }

    /// <summary>The Syntax AST node representing the current entity instance.</summary>
    public Node Subject { get; }

    /// <summary>
    /// Lowers <paramref name="effect"/> to a Syntax AST node suitable for VM
    /// compilation, or returns <c>null</c> when the effect must be executed
    /// directly on a <see cref="DomainEntityInstance"/>.
    /// </summary>
    public Node? TryLowerVmNode(Effect effect) => Route(effect);

    protected override Node? Default() => null;

    protected override Node? Assign(AssignEffect a) {
        var target = _expressionPass.Lower(a.Target, Subject);
        var value = _expressionPass.Lower(a.Value, Subject);
        return new Assignment(target, value);
    }

    /// <summary>
    /// Lowers a CompositeEffect. Only VM-compilable sub-effects are included;
    /// direct-execution sub-effects (which return null) are recorded as
    /// <see cref="Comment"/> nodes so the lowered AST preserves information
    /// about what was not lowered. The Syntax AST's Block requires at least
    /// one expression (type inference constraint), so Comments serve as
    /// both documentation and a structural placeholder.
    /// </summary>
    protected override Node? Composite(CompositeEffect c) {
        var nodes = new List<Node>();
        foreach (var sub in c.Effects) {
            var lowered = Route(sub);
            if (lowered is not null)
                nodes.Add(lowered);
            else
                nodes.Add(new Comment($"Cannot lower: {sub.GetType().Name}"));
        }
        return new Block(nodes);
    }

    protected override Node? Conditional(ConditionalEffect c) {
        var condition = _expressionPass.Lower(c.Condition, Subject);
        var thenNodes = new List<Node>();
        foreach (var sub in c.ThenEffects) {
            var lowered = Route(sub);
            thenNodes.Add(lowered ?? new Comment($"Cannot lower: {sub.GetType().Name}"));
        }

        if (c.ElseEffects is not { Count: > 0 })
            return new IfStatement(condition, new Block(thenNodes));

        var elseNodes = new List<Node>();
        foreach (var sub in c.ElseEffects) {
            var lowered = Route(sub);
            elseNodes.Add(lowered ?? new Comment($"Cannot lower: {sub.GetType().Name}"));
        }

        return new IfStatement(condition, new Block(thenNodes), new Block(elseNodes));
    }
}