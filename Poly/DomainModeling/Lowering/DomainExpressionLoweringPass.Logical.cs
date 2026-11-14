using Poly.Ast.Nodes;
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

        // Value hops (`Age < advisor Age`) do not get the predicate-shape
        // `rel != null && leaf` from RelationshipNavigation. Guard each to-one hop
        // that an operand reads through from this comparison's subject, on both
        // sides, so an unlinked hop makes the comparison false instead of
        // dereferencing null. Guards are folded inner-to-outer so each
        // dereference is protected by the guard before it. A hop reached twice
        // (`Age < advisor Age + advisor mentor Age`) is guarded once.
        var valueHops = new List<Node>();
        var seenPaths = new HashSet<string>(StringComparer.Ordinal);
        CollectValueHops(c.Left, _currentSubject, "", valueHops, seenPaths);
        CollectValueHops(c.Right, _currentSubject, "", valueHops, seenPaths);
        for (var i = valueHops.Count - 1; i >= 0; i--)
            value = new SN.And(new NotEqual(valueHops[i], new Constant(null)), value);

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
    /// </summary>
    private LoweredExpression RouteCondition(DomainExpression condition) {
        var lowered = Route(condition);
        return IsOptionalBoolean(condition)
            ? lowered with { Value = new Equal(lowered.Value, new Constant(true)) }
            : lowered;
    }

    private bool IsOptionalBoolean(DomainExpression expr) =>
        _analysis.GetMetadata<TypedExpressionMetadata>(expr) is { Type: { Optional: true, Definition.PrimitiveType: Introspection.PrimitiveType.Boolean } };

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

    /// <summary>
    /// Collects the to-one hops an operand reads through from <paramref name="subject"/>
    /// (the comparison's subject, or the hop a navigation's target is read on):
    /// <c>advisor mentor Age</c> and <c>advisor where mentor Age * 2</c> both yield
    /// <c>.Advisor</c> and <c>.Advisor.Mentor</c>. Arithmetic and
    /// <c>not</c>/<c>and</c>/<c>or</c> are looked through; any other operand adds no
    /// hop (a quantifier body runs against its loop item and guards its own
    /// comparisons). The chain stops at a subscription binder name
    /// (<c>order Total</c>) and at a navigation to a condition
    /// (<c>advisor where Age &lt; 30</c>): lowering guards that navigation's own
    /// hop, and hops inside the condition are guarded by the comparisons there.
    /// Known issue: lowering treats a binder name as the binder at any depth, so
    /// <c>advisor order Total</c> reads the binder, not the advisor's order. The
    /// chain matches lowering rather than guarding a hop lowering never reads.
    /// </summary>
    /// <param name="subjectPath">Dotted member path of <paramref name="subject"/>, used to
    /// recognise the same hop reached twice (nodes compare by identity, not shape).</param>
    private void CollectValueHops(
        DomainExpression operand, Node subject, string subjectPath,
        List<Node> hops, HashSet<string> seenPaths) {
        switch (operand) {
            case Ontology.Add or Ontology.Subtract or Ontology.Multiply or Ontology.Divide
                or Ontology.Not or Ontology.And or Ontology.Or:
                foreach (var child in operand.Children.OfType<DomainExpression>())
                    CollectValueHops(child, subject, subjectPath, hops, seenPaths);
                break;
            case RelationshipNavigation rn:
                CollectHopChain(rn, subject, subjectPath, hops, seenPaths);
                break;
        }
    }

    private void CollectHopChain(
        RelationshipNavigation rn, Node subject, string subjectPath,
        List<Node> hops, HashSet<string> seenPaths) {
        if (_parameters.ContainsKey(rn.RelationshipName)
            || IsCollectionNav(rn.RelationshipName)
            || IsPathPrefixPredicate(rn.TargetProperty))
            return;
        var navName = ResolveNavName(rn.RelationshipName);
        var hopPath = subjectPath + "." + navName;
        var hop = new Member(subject, navName);
        if (seenPaths.Add(hopPath))
            hops.Add(hop);
        CollectValueHops(rn.TargetProperty, hop, hopPath, hops, seenPaths);
    }
}