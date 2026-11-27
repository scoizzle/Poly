using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Ontology;

using And = Poly.DomainModeling.Ontology.And;
using Not = Poly.DomainModeling.Ontology.Not;
using Or = Poly.DomainModeling.Ontology.Or;
using SN = Poly.Ast.Nodes;

namespace Poly.DomainModeling.Lowering;

// Logical: Comparison (with enum-literal and value-hop guards), And, Or, Not.

public sealed partial class DomainExpressionLoweringPass {
    protected override LoweredExpression Comparison(Comparison c) {
        // An enum-typed property on one side decides the other side's type: a string
        // literal or a bare member name becomes qualified member access, through the
        // one target-type adapter. The property side adapts to itself, which the
        // shadow guard leaves as an instance read.
        var left = EnumTargetOf(c.Right) is { } leftTarget
            ? AdaptTo(c.Left, leftTarget, _currentSubject)
            : Route(c.Left);
        var right = EnumTargetOf(c.Left) is { } rightTarget
            ? AdaptTo(c.Right, rightTarget, _currentSubject)
            : Route(c.Right);
        var loweredLeft = left.Value;
        var loweredRight = right.Value;

        // A DateTime ordering compares instants, not clock times (#569): a Local value is its UTC instant.
        var presenceGuards = new List<Node>();
        if (IsInstantOrdering(c.Kind)) {
            loweredLeft = AsUtcInstant(c.Left, loweredLeft, presenceGuards);
            loweredRight = AsUtcInstant(c.Right, loweredRight, presenceGuards);
        }

        // Count is an int; compare it to an int literal so the printed form is
        // `Count > 0`, not `Count > 0L` (G18). Sum accumulators stay long.
        if (IsCount(loweredLeft)
            && loweredRight is Constant { Value: long leftCount }) {
            loweredRight = CountConstant(leftCount);
        }
        else if (IsCount(loweredRight)
            && loweredLeft is Constant { Value: long rightCount }) {
            loweredLeft = CountConstant(rightCount);
        }

        // Simplify boolean comparisons: boolProp == true  → boolProp
        //                            boolProp == false → !boolProp
        // An optional Boolean keeps the lifted comparison: `Flag is false` is false when Flag is absent.
        Node value;
        var foldable = !IsOptionalBoolean(c.Left);
        if (foldable && c.Kind == ComparisonKind.Equal
            && loweredRight is Constant { Value: bool b }) {
            value = b ? loweredLeft : new SN.Not(loweredLeft);
        }
        else if (foldable && c.Kind == ComparisonKind.NotEqual
            && loweredRight is Constant { Value: bool b2 }) {
            value = b2 ? new SN.Not(loweredLeft) : loweredLeft;
        }
        else {
            value = c.Kind switch {
                ComparisonKind.Equal => new Equal(loweredLeft, loweredRight),
                ComparisonKind.NotEqual => new NotEqual(loweredLeft, loweredRight),
                ComparisonKind.LessThan => new LessThan(loweredLeft, loweredRight),
                ComparisonKind.LessThanOrEqual => new LessThanOrEqual(loweredLeft, loweredRight),
                ComparisonKind.GreaterThan => new GreaterThan(loweredLeft, loweredRight),
                ComparisonKind.GreaterThanOrEqual => new GreaterThanOrEqual(loweredLeft, loweredRight),
                _ => throw new NotSupportedException($"Comparison kind '{c.Kind}' is not supported."),
            };
        }

        // An optional DateTime is ordered only when it is present: C#'s lifted rule for `<` and friends.
        for (var i = presenceGuards.Count - 1; i >= 0; i--)
            value = new SN.And(presenceGuards[i], value);

        // A read through an unlinked to-one hop yields absent (#936), the same value an omitted optional
        // property has, so the comparison answers the guide's optional-value rule: `==` is true only when
        // both sides are absent, `!=` is true when exactly one is, and an ordering is false. The guard
        // precedes the operands inside `&&`, so a dereference is never evaluated on the absent side. When
        // no operand reads through a hop the comparison is unchanged: C# already lifts an optional value.
        var leftPresence = PresenceOf(c.Left, _currentSubject);
        var rightPresence = PresenceOf(c.Right, _currentSubject);
        if (leftPresence.ThroughHop || rightPresence.ThroughHop)
            value = AbsentAware(c, leftPresence.Guard, rightPresence.Guard, value);

        return LoweredExpression.Combine([left, right], value);
    }

    protected override LoweredExpression And(And a) {
        var left = RouteCondition(a.Left);
        var right = RouteCondition(a.Right);
        if (right.Statements.Count == 0)
            return LoweredExpression.Combine([left, right], new SN.And(left.Value, right.Value));
        return ShortCircuit(left, right, whenTrue: true);
    }

    protected override LoweredExpression Or(Or o) {
        var left = RouteCondition(o.Left);
        var right = RouteCondition(o.Right);
        if (right.Statements.Count == 0)
            return LoweredExpression.Combine([left, right], new SN.Or(left.Value, right.Value));
        return ShortCircuit(left, right, whenTrue: false);
    }

    protected override LoweredExpression Not(Not n) {
        var operand = RouteCondition(n.Operand);
        return operand with { Value = new SN.Not(operand.Value) };
    }

    /// <summary>
    /// Lowers a condition: an <c>if</c>, a <c>for … where</c>, a policy root, or (through <see cref="RouteCondition"/>)
    /// an <c>and</c>/<c>or</c>/<c>not</c> operand, a <c>match</c> arm condition and a quantifier body.
    /// </summary>
    internal LoweredExpression LowerCondition(DomainExpression condition, Node subject) {
        _currentSubject = subject;
        return RouteCondition(condition);
    }

    /// <summary>
    /// An optional Boolean in a condition position means <c>X == true</c> (T-06, #862): stored false and absent are
    /// both false, on both hosts, as the lifted comparison. The analyzer stamps such an operand
    /// (<c>ExpressionTypeAnalyzer.StampOptionalBoolean</c>, beside each of its Boolean-position checks), so the two
    /// lists of positions are one list. An invoke or function argument is an assignment, not a condition: never here.
    /// A read through an unlinked to-one hop is absent (#936), so its guard precedes the read: the condition is false,
    /// and <c>not</c> over it is true, as the guide states for an absent optional Boolean.
    /// </summary>
    private LoweredExpression RouteCondition(DomainExpression condition) {
        var lowered = Route(condition);
        if (IsOptionalBoolean(condition))
            lowered = lowered with { Value = new Equal(lowered.Value, new Constant(true)) };
        var presence = PresenceOf(condition, _currentSubject);
        return presence.ThroughHop && presence.Guard is not null
            ? lowered with { Value = And(presence.Guard, lowered.Value) }
            : lowered;
    }

    private bool IsOptionalBoolean(DomainExpression expr) =>
        _analysis.GetMetadata<TypedExpressionMetadata>(expr) is { Type: { Optional: true, Definition: { } definition } }
        && Poly.DomainModeling.Meaning.DomainTypeSystem.WordOf(definition) is "Boolean";

    /// <summary>
    /// Preserves source-order short-circuit when the right operand has statements:
    /// evaluate left, then run the right statements only if the left requires it
    /// (<c>if (t)</c> for and, <c>if (!t)</c> for or).
    /// </summary>
    private LoweredExpression ShortCircuit(LoweredExpression left, LoweredExpression right, bool whenTrue) {
        var t = _names.Next("t");
        Node gate = whenTrue ? t : new SN.Not(t);
        var thenBlock = new Block(
            [.. right.Statements, new Assignment(t, right.Value)],
            right.Variables);
        List<Node> statements = [.. left.Statements, new Assignment(t, left.Value), new IfStatement(gate, thenBlock)];
        return new LoweredExpression(statements, [.. left.Variables, t], t);
    }

    private static bool IsCount(Node node) =>
        node is Member { MemberName: "Count" };

    private static Node CountConstant(long value) =>
        value is >= int.MinValue and <= int.MaxValue
            ? new Constant((int)value)
            : new Constant(value);
}