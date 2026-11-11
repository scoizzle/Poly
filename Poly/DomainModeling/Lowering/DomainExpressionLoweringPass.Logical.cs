using Poly.Ast.Nodes;
using Poly.DomainModeling.Ontology;

using And = Poly.DomainModeling.Ontology.And;
using Not = Poly.DomainModeling.Ontology.Not;
using Or = Poly.DomainModeling.Ontology.Or;
using SN = Poly.Ast.Nodes;

namespace Poly.DomainModeling.Lowering;

// Logical: Comparison (with enum-literal and value-hop guards), And, Or, Not.

public sealed partial class DomainExpressionLoweringPass {
    protected override LoweredExpression Comparison(Comparison c) {
        var left = Route(c.Left);
        var right = Route(c.Right);
        var loweredLeft = left.Value;
        var loweredRight = right.Value;

        // For enum-typed properties, replace string literal with qualified member
        // access: Status == "Active" becomes Status == PatronStatus.Active
        if (_enumPropertyNames is { Count: > 0 }) {
            var fixedLeft = FixEnumLiteral(c.Left, c.Right, loweredRight);
            var fixedRight = FixEnumLiteral(c.Right, c.Left, loweredLeft);
            if (fixedLeft is not null) loweredLeft = fixedLeft;
            if (fixedRight is not null) loweredRight = fixedRight;
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
        Node value;
        if (c.Kind == ComparisonKind.Equal
            && loweredRight is Constant { Value: bool b }) {
            value = b ? loweredLeft : new SN.Not(loweredLeft);
        }
        else if (c.Kind == ComparisonKind.NotEqual
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
        var left = Route(a.Left);
        var right = Route(a.Right);
        if (right.Statements.Count == 0)
            return LoweredExpression.Combine([left, right], new SN.And(left.Value, right.Value));
        return ShortCircuit(left, right, whenTrue: true);
    }

    protected override LoweredExpression Or(Or o) {
        var left = Route(o.Left);
        var right = Route(o.Right);
        if (right.Statements.Count == 0)
            return LoweredExpression.Combine([left, right], new SN.Or(left.Value, right.Value));
        return ShortCircuit(left, right, whenTrue: false);
    }

    protected override LoweredExpression Not(Not n) {
        var operand = Route(n.Operand);
        return operand with { Value = new SN.Not(operand.Value) };
    }
}