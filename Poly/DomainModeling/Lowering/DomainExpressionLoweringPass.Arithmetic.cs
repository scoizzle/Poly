using Add = Poly.DomainModeling.Ontology.Add;
using Divide = Poly.DomainModeling.Ontology.Divide;
using Multiply = Poly.DomainModeling.Ontology.Multiply;
using SN = Poly.Ast.Nodes;
using Subtract = Poly.DomainModeling.Ontology.Subtract;

namespace Poly.DomainModeling.Lowering;

// Arithmetic expressions: Add, Subtract, Multiply, Divide. Library handlers get first refusal.

public sealed partial class DomainExpressionLoweringPass {
    protected override LoweredExpression Add(Add a) {
        if (TryLowerLibrary(a, out var lowered))
            return lowered;
        var left = Route(a.Left);
        var right = Route(a.Right);
        return LoweredExpression.Combine([left, right], new SN.Add(left.Value, right.Value));
    }

    protected override LoweredExpression Subtract(Subtract s) {
        if (TryLowerLibrary(s, out var lowered))
            return lowered;
        var left = Route(s.Left);
        var right = Route(s.Right);
        return LoweredExpression.Combine([left, right], new SN.Subtract(left.Value, right.Value));
    }

    protected override LoweredExpression Multiply(Multiply m) {
        var left = Route(m.Left);
        var right = Route(m.Right);
        return LoweredExpression.Combine([left, right], new SN.Multiply(left.Value, right.Value));
    }

    protected override LoweredExpression Divide(Divide d) {
        var left = Route(d.Left);
        var right = Route(d.Right);
        return LoweredExpression.Combine([left, right], new SN.Divide(left.Value, right.Value));
    }
}