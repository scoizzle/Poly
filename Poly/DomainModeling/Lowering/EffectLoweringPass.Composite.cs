using Poly.Ast.Nodes;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Dispatch;
using Poly.DomainModeling.Meaning;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;

using Syntactic = Poly.Ast.Nodes;

namespace Poly.DomainModeling.Lowering;

// Composite and Conditional: the structural control-flow effect kinds.
public sealed partial class EffectLoweringPass {
    /// <summary>
    /// Lowers a CompositeEffect. Every sub-effect must lower — a sub-effect that
    /// cannot be lowered (unknown create target, unresolved relationship) is a
    /// fail-closed error, never a silent drop. Mixed if+create lowers on the
    /// runtime path via instance factories (not ExecuteStructured).
    /// </summary>
    protected override Node? Composite(CompositeEffect c) {
        var nodes = new List<Node>();
        var variables = new List<Node>();
        foreach (var sub in c.Effects) {
            var lowered = Route(sub);
            if (lowered is null)
                throw new InvalidOperationException(
                    $"Cannot lower effect '{DescribeEffect(sub)}' to a Syntax AST node.");
            CollectNode(nodes, variables, lowered);
        }
        return new Block(nodes, variables);
    }
    protected override Node? Conditional(ConditionalEffect c) {
        var condition = _expressionPass.LowerExpression(c.Condition, Subject);
        var canTransition = CanChangeStage(c.ThenEffects)
            || (c.ElseEffects is { Count: > 0 } && CanChangeStage(c.ElseEffects));
        var entryStage = _sourceStageName;
        var thenNodes = new List<Node>();
        var thenVars = new List<Node>();
        foreach (var sub in c.ThenEffects) {
            var lowered = Route(sub);
            if (lowered is null)
                throw new InvalidOperationException(
                    $"Cannot lower effect '{DescribeEffect(sub)}' to a Syntax AST node.");
            CollectNode(thenNodes, thenVars, lowered);
        }

        if (c.ElseEffects is not { Count: > 0 }) {
            ClearCursorIf(canTransition);
            return condition.Before(new IfStatement(condition.Value, new Block(thenNodes, thenVars)));
        }

        // The else branch runs only when the then-branch did not, so it starts from the
        // stage the conditional was entered in, not the one a then-hop advanced to.
        _sourceStageName = entryStage;
        var elseNodes = new List<Node>();
        var elseVars = new List<Node>();
        foreach (var sub in c.ElseEffects) {
            var lowered = Route(sub);
            if (lowered is null)
                throw new InvalidOperationException(
                    $"Cannot lower effect '{DescribeEffect(sub)}' to a Syntax AST node.");
            CollectNode(elseNodes, elseVars, lowered);
        }

        ClearCursorIf(canTransition);
        return condition.Before(new IfStatement(
            condition.Value, new Block(thenNodes, thenVars), new Block(elseNodes, elseVars)));
    }

    /// <summary>
    /// A hop inside a branch may not run, so the stage after the branch is not statically
    /// known: null the cursor (the per-stage exit switch) instead of leaving the guarded
    /// hop's target behind. The same dynamic cursor a <c>for</c> body uses.
    /// </summary>
    private void ClearCursorIf(bool canTransition) {
        if (canTransition)
            _sourceStageName = null;
    }

    /// <summary>
    /// True when the list holds a transition or a self-invoke (whose callee may transition,
    /// directly or transitively), however deeply nested.
    /// </summary>
    private static bool CanChangeStage(IEnumerable<Effect> effects) =>
        EffectHelpers.FlattenEffects(effects).Any(e =>
            e is StageTransitionEffect or InvokeActionEffect { TargetRelationship: null });
}