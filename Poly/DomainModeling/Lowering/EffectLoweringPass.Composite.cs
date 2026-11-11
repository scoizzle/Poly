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
        var thenNodes = new List<Node>();
        var thenVars = new List<Node>();
        foreach (var sub in c.ThenEffects) {
            var lowered = Route(sub);
            if (lowered is null)
                throw new InvalidOperationException(
                    $"Cannot lower effect '{DescribeEffect(sub)}' to a Syntax AST node.");
            CollectNode(thenNodes, thenVars, lowered);
        }

        if (c.ElseEffects is not { Count: > 0 })
            return condition.Before(new IfStatement(condition.Value, new Block(thenNodes, thenVars)));

        var elseNodes = new List<Node>();
        var elseVars = new List<Node>();
        foreach (var sub in c.ElseEffects) {
            var lowered = Route(sub);
            if (lowered is null)
                throw new InvalidOperationException(
                    $"Cannot lower effect '{DescribeEffect(sub)}' to a Syntax AST node.");
            CollectNode(elseNodes, elseVars, lowered);
        }

        return condition.Before(new IfStatement(
            condition.Value, new Block(thenNodes, thenVars), new Block(elseNodes, elseVars)));
    }
}