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
/// through the VM — they produce <c>null</c> from <see cref="TryLowerVmNode"/>
/// and are handled by the caller.</para>
/// </summary>
public sealed class EffectLoweringPass {
    private readonly Entity _entity;
    private readonly DomainExpressionLoweringPass _expressionPass;

    public EffectLoweringPass(Entity entity, Node subject) {
        _entity = entity;
        _expressionPass = new DomainExpressionLoweringPass();
        Subject = subject;
    }

    /// <summary>The Syntax AST node representing the current entity instance.</summary>
    public Node Subject { get; }

    /// <summary>
    /// Lowers <paramref name="effect"/> to a Syntax AST node suitable for VM
    /// compilation, or returns <c>null</c> when the effect must be executed
    /// directly on a <see cref="DomainEntityInstance"/>.
    /// </summary>
    public Node? TryLowerVmNode(Effect effect) {
        return effect switch {
            AssignEffect a => LowerAssign(a),
            CompositeEffect c => LowerComposite(c),
            ConditionalEffect c => LowerConditional(c),
            _ => null // direct-execution effects — handled by DomainEntityInstance.InvokeAction
        };
    }

    private Assignment LowerAssign(AssignEffect effect) {
        var target = _expressionPass.Lower(effect.Target, Subject);
        var value = _expressionPass.Lower(effect.Value, Subject);
        return new Assignment(target, value);
    }

    private Block LowerComposite(CompositeEffect effect) {
        var nodes = new List<Node>();
        foreach (var sub in effect.Effects) {
            var lowered = TryLowerVmNode(sub);
            if (lowered is not null)
                nodes.Add(lowered);
        }
        return new Block(nodes.Count > 0 ? nodes : [new Constant(0L)]);
    }

    private Node LowerConditional(ConditionalEffect effect) {
        var condition = _expressionPass.Lower(effect.Condition, Subject);
        var thenNodes = new List<Node>();
        foreach (var sub in effect.ThenEffects) {
            var lowered = TryLowerVmNode(sub);
            if (lowered is not null) thenNodes.Add(lowered);
        }
        var thenBlock = new Block(thenNodes.Count > 0 ? thenNodes : [new Constant(0L)]);

        if (effect.ElseEffects is not { Count: > 0 })
            return new IfStatement(condition, thenBlock);

        var elseNodes = new List<Node>();
        foreach (var sub in effect.ElseEffects) {
            var lowered = TryLowerVmNode(sub);
            if (lowered is not null) elseNodes.Add(lowered);
        }
        return new IfStatement(condition, thenBlock,
            new Block(elseNodes.Count > 0 ? elseNodes : [new Constant(0L)]));
    }
}