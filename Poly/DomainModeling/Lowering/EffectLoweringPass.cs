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

    protected override Node? Composite(CompositeEffect c) {
        var nodes = new List<Node>();
        foreach (var sub in c.Effects) {
            var lowered = Route(sub);
            if (lowered is not null)
                nodes.Add(lowered);
        }
        return new Block(nodes.Count > 0 ? nodes : [new Constant(0L)]);
    }

    protected override Node? Conditional(ConditionalEffect c) {
        var condition = _expressionPass.Lower(c.Condition, Subject);
        var thenNodes = new List<Node>();
        foreach (var sub in c.ThenEffects) {
            var lowered = Route(sub);
            if (lowered is not null) thenNodes.Add(lowered);
        }
        var thenBlock = new Block(thenNodes.Count > 0 ? thenNodes : [new Constant(0L)]);

        if (c.ElseEffects is not { Count: > 0 })
            return new IfStatement(condition, thenBlock);

        var elseNodes = new List<Node>();
        foreach (var sub in c.ElseEffects) {
            var lowered = Route(sub);
            if (lowered is not null) elseNodes.Add(lowered);
        }
        return new IfStatement(condition, thenBlock,
            new Block(elseNodes.Count > 0 ? elseNodes : [new Constant(0L)]));
    }
}