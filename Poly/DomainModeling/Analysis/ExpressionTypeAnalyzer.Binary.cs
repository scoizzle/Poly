using Poly.DomainModeling.Meaning;
using Poly.DomainModeling.Ontology;
using Poly.Introspection;

namespace Poly.DomainModeling.Analysis;

// Binary typing: the stamp an arithmetic node carries. The analyzer states it and never gates on it:
// acceptance stays the family rules in ExpressionTypeRules.cs, and an absent stamp means the analyzer did not
// state an operator (never "operator undefined", never "unknown type"). T-07 makes GetOperator the gate.

internal sealed partial class ExpressionTypeAnalyzer : INodeAnalyzer {
    private void StampBinary(
        AnalysisContext context, DomainExpression node, OperatorKind kind,
        DomainExpression left, DomainExpression right,
        Dictionary<string, string> props, Dictionary<string, string>? parameters,
        Dictionary<string, EnumType> enumTypes) {
        // A library stamp (or an earlier walk's) wins; the second walk of a shared `require` tree is a no-op.
        if (context.GetMetadata<TypedExpressionMetadata>(node) is not null) return;
        if (ModelOf(context) is not { } model) return;
        var leftType = InferType(context, left, props, parameters, enumTypes);
        var rightType = InferType(context, right, props, parameters, enumTypes);
        var leftKind = model.KindOf(leftType);
        var rightKind = model.KindOf(rightType);
        if (leftKind is NumericKind.None || rightKind is NumericKind.None) return;
        if (leftType.Definition!.GetOperator(kind, rightType.Definition) is not { } resolution) return;

        var result = new ExprType(resolution.Result, Optional: false, DomainName: null);
        var integer = leftKind is NumericKind.Integer64 && rightKind is NumericKind.Integer64;
        context.SetMetadata(node, new TypedExpressionMetadata(result, new BinaryTyping(
            result,
            model.KindOf(result),
            IsIntegerDivision: kind is OperatorKind.Divide && integer,
            MayOverflow: kind is OperatorKind.Add or OperatorKind.Subtract or OperatorKind.Multiply && integer,
            Checked: false)));
    }
}
