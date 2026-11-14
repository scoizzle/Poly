using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Ontology;

using Prim = Poly.Introspection.PrimitiveType;
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
    /// The one chain builder. A <c>match</c> has one type, the one analysis stamped (<see cref="MatchTypingMetadata"/>): every
    /// arm is converted to it explicitly, so a Decimal arm and a Number arm share one representation (T-09a). A non-null
    /// <paramref name="target"/> adapts every arm value to the slot the chain is lowered into
    /// (<c>assign Tier to match { … -> Gold }</c> prints qualified members); the bare <c>match</c> form has no target and
    /// lowers each arm normally. The adapter never builds a chain of its own — it calls here. A match that was never
    /// analysed has no type and does not lower.
    /// </summary>
    internal LoweredExpression LowerMatch(MatchExpr m, DomainTypeReference? target) {
        var typing = _analysis.GetMetadata<MatchTypingMetadata>(m)
            ?? throw new InvalidOperationException(
                $"'match' has no analysed type (line {m.Line}, col {m.Col}): its arms cannot be converted to one type, so it does not lower.");
        // A match that can be absent (an arm is `null` or optional) fills a local of the optional type instead of a
        // conditional: `(T?)null` cannot share one word with a present arm. Analysis refuses a `null` arm into a required
        // slot (#606). The slot is the target's, or the match's own type when it has no target (an operand, an argument).
        var slot = typing.Unified.Optional ? SlotOf(typing.Unified, target) : null;
        var toDecimal = typing.Unified.Definition?.PrimitiveType is Prim.Decimal
            || slot is not null && target is not null && TargetTypeAdapter.IsDecimal(_domain, target.TypeName);
        var arms = m.Arms
            .Select((a, i) => (Condition: RouteCondition(a.Condition), Value: Arm(a.Value, target, typing.ArmTypes[i], toDecimal, slot)))
            .ToList();
        var otherwise = Arm(m.Else, target, typing.ArmTypes[^1], toDecimal, slot);
        if (slot is not null) {
            var result = _names.Next("match");
            // A nested match that can be absent fills its own local with statements of its own: they run inside the branch of the arm
            // that holds it, so an inner arm is not evaluated when the outer arm is not taken (as the conditional chain did).
            bool Lazy(DomainExpression arm) =>
                arm is MatchExpr inner && _analysis.GetMetadata<MatchTypingMetadata>(inner) is { Unified.Optional: true };
            LoweredExpression Hoisted(LoweredExpression lowered, DomainExpression arm) =>
                Lazy(arm) ? lowered with { Statements = [] } : lowered;
            Node Take(LoweredExpression lowered, DomainExpression arm) =>
                new Block([.. Lazy(arm) ? lowered.Statements : [], new Assignment(result, lowered.Value)]);
            Node chain = Take(otherwise, m.Else);
            for (var i = arms.Count - 1; i >= 0; i--)
                chain = new IfStatement(arms[i].Condition.Value, Take(arms[i].Value, m.Arms[i].Value), new Block([chain]));
            var parts = LoweredExpression.Combine(
                [.. arms.SelectMany((a, i) => new[] { a.Condition, Hoisted(a.Value, m.Arms[i].Value) }), Hoisted(otherwise, m.Else)], result);
            return new LoweredExpression(
                [.. parts.Statements, new Assignment(result, new SN.TypeCast(new Constant(null), slot)), chain], [.. parts.Variables, result], result);
        }
        var value = otherwise.Value;
        for (var i = arms.Count - 1; i >= 0; i--)
            value = new SN.Conditional(arms[i].Condition.Value, arms[i].Value.Value, value);
        return LoweredExpression.Combine(
            [.. arms.SelectMany(a => new[] { a.Condition, a.Value }), otherwise], value);
    }

    /// <summary>
    /// The optional type of the local an absent-able match fills: the target's own (an enum or primitive slot), else the
    /// match's unified type; null for any other type, which keeps the conditional.
    /// </summary>
    private SN.OptionalTypeReference? SlotOf(ExprType unified, DomainTypeReference? target) {
        if (target is not null && TargetTypeAdapter.OptionalSlot(target, _context) is { } fromTarget)
            return fromTarget;
        var name = unified.DomainName ?? unified.Definition?.PrimitiveType switch {
            Prim.Int64 => "Number",
            Prim.Decimal => "Decimal",
            Prim.String => "Text",
            Prim.Boolean => "Boolean",
            Prim.Guid => "Uuid",
            Prim.DateOnly => "Date",
            Prim.DateTime => "DateTime",
            Prim.TimeOnly => "Time",
            Prim.TimeSpan => "Duration",
            _ => null,
        };
        return name is null ? null : TargetTypeAdapter.OptionalSlot(new DomainTypeReference(name), _context);
    }

    /// <summary>
    /// One arm lowered to the match's type. A conditional arm does not ride the analyzer's implicit-conversion rewrite (that
    /// wraps an <c>Assignment</c>, not a <c>Conditional</c> arm), so a present integer arm of a Decimal match is cast to
    /// <c>decimal</c> here (#605, #623): a scalar word and a Decimal handle never meet in one conditional. A <c>null</c> arm
    /// is the slot's own absent value. The arm's hoisted statements are kept.
    /// </summary>
    private LoweredExpression Arm(
        DomainExpression value, DomainTypeReference? target, ExprType armType, bool toDecimal, SN.OptionalTypeReference? slot) {
        var lowered = value is Literal { Value: null } && slot is not null
            ? LoweredExpression.Of(new SN.TypeCast(new Constant(null), slot))
            : target is null ? Route(value) : AdaptTo(value, target, _currentSubject);
        return toDecimal && !armType.Optional && armType.Definition?.PrimitiveType is Prim.Int32 or Prim.Int64
            ? lowered with { Value = new SN.TypeCast(lowered.Value, new SN.PrimitiveTypeReference(Prim.Decimal)) }
            : lowered;
    }
}