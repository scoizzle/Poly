using Poly.DomainModeling.Ontology;

using SN = Poly.Ast.Nodes;

namespace Poly.DomainModeling.Lowering;

// match expressions: right-nested Conditional chain with the else value innermost.

public sealed partial class DomainExpressionLoweringPass {
    /// <summary>
    /// <c>match</c> (#409) lowers to a right-nested <see cref="Conditional"/> chain with the
    /// <c>else</c> value innermost, so simulation and printed C# share one conditional. Statements
    /// hoisted by an arm (quantifier loops) are read-only and run before the chain.
    /// </summary>
    protected override LoweredExpression MatchExpr(MatchExpr m) {
        var arms = m.Arms.Select(a => (Condition: Route(a.Condition), Value: Route(a.Value))).ToList();
        var otherwise = Route(m.Else);
        var value = otherwise.Value;
        for (var i = arms.Count - 1; i >= 0; i--)
            value = new SN.Conditional(arms[i].Condition.Value, arms[i].Value.Value, value);
        return LoweredExpression.Combine(
            [.. arms.SelectMany(a => new[] { a.Condition, a.Value }), otherwise], value);
    }
}