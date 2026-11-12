using Poly.DomainModeling.Ontology;

using SN = Poly.Ast.Nodes;

namespace Poly.DomainModeling.Lowering;

// match expressions: right-nested Conditional chain with the else value innermost.

public sealed partial class DomainExpressionLoweringPass {
    /// <summary>
    /// <c>match</c> (#409) lowers to a right-nested <see cref="SN.Conditional"/> chain with the
    /// <c>else</c> value innermost, so simulation and printed C# share one conditional. Statements
    /// hoisted by an arm (quantifier loops) are read-only and run before the chain.
    /// </summary>
    protected override LoweredExpression MatchExpr(MatchExpr m) => LowerMatch(m, target: null);

    /// <summary>
    /// The one chain builder. A non-null <paramref name="target"/> adapts every arm value
    /// to the slot the chain is lowered into (<c>assign Tier to match { … -> Gold }</c>
    /// prints qualified members); the bare <c>match</c> form has no target and lowers
    /// each arm normally. The adapter never builds a chain of its own — it calls here.
    /// </summary>
    internal LoweredExpression LowerMatch(MatchExpr m, DomainTypeReference? target) {
        var arms = m.Arms
            .Select(a => (Condition: Route(a.Condition), Value: Arm(a.Value, target)))
            .ToList();
        var otherwise = Arm(m.Else, target);
        var value = otherwise.Value;
        for (var i = arms.Count - 1; i >= 0; i--)
            value = new SN.Conditional(arms[i].Condition.Value, arms[i].Value.Value, value);
        return LoweredExpression.Combine(
            [.. arms.SelectMany(a => new[] { a.Condition, a.Value }), otherwise], value);
    }

    /// <summary>
    /// A conditional arm does not ride the analyzer's implicit-conversion rewrite (that wraps an
    /// <c>Assignment</c>, not a <c>Conditional</c> arm), so a whole-number literal written for a Decimal
    /// slot is retyped here: the VM would otherwise merge a scalar word with a Decimal handle (#605).
    /// The arm's hoisted statements are kept.
    /// </summary>
    private LoweredExpression Arm(DomainExpression value, DomainTypeReference? target) {
        if (target is null)
            return Route(value);
        var lowered = AdaptTo(value, target, _currentSubject);
        return lowered with { Value = TargetTypeAdapter.Adapt(lowered.Value, target, _context) };
    }
}