using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Analysis;

// Arithmetic arms: CheckArithmetic (Add/Subtract) and CheckNumericArithmetic (Multiply/Divide); the Date - Date case stays inline in core's WalkExpression case Subtract.

internal sealed partial class ExpressionTypeAnalyzer : INodeAnalyzer {
    private void CheckArithmetic(
        AnalysisContext context, DomainExpression left, DomainExpression right,
        Dictionary<string, string> props, Dictionary<string, string>? parameters,
        Dictionary<string, EnumType> enumTypes,
        bool allowTextConcat = false) {
        var leftType = InferType(context, left, props, parameters, enumTypes);
        var rightType = InferType(context, right, props, parameters, enumTypes);
        ReportFractionalLiteral(context, left, leftType, right, rightType);
        // numeric + numeric, text + text (concatenation; `+` only), date + number (AddDays
        // lowering), or date + duration
        // (a parsed `N days` offset with a temporal left operand); Unknown operands
        // (path-prefix reads, peer binders) are out of this scope — skip. A duration
        // without a temporal left operand (Number + days, days + date) is an unresolved
        // temporal specialization — reject, never a silent numeric constant.
        if (leftType.Category is not TypeCategory.Unknown && rightType.Category is not TypeCategory.Unknown
            && !(IsNumeric(leftType.Category) && IsNumeric(rightType.Category))
            && !(allowTextConcat && leftType.Category is TypeCategory.Text && rightType.Category is TypeCategory.Text)
            && !(IsDate(leftType.Category) && IsNumeric(rightType.Category))
            && !(IsDate(leftType.Category) && rightType.Category is TypeCategory.Duration))
            Report(context, left,
                $"arithmetic operand is not numeric (got '{Describe(leftType)}' and '{Describe(rightType)}')");
        WalkExpression(context, left, props, parameters, enumTypes);
        WalkExpression(context, right, props, parameters, enumTypes);
    }

    private void CheckNumericArithmetic(
        AnalysisContext context, DomainExpression left, DomainExpression right,
        Dictionary<string, string> props, Dictionary<string, string>? parameters,
        Dictionary<string, EnumType> enumTypes) {
        var leftType = InferType(context, left, props, parameters, enumTypes);
        var rightType = InferType(context, right, props, parameters, enumTypes);
        ReportFractionalLiteral(context, left, leftType, right, rightType);
        if (leftType.Category is not TypeCategory.Unknown && rightType.Category is not TypeCategory.Unknown
            && (!IsNumeric(leftType.Category) || !IsNumeric(rightType.Category)))
            Report(context, left,
                $"arithmetic operand is not numeric (got '{Describe(leftType)}' and '{Describe(rightType)}')");
        WalkExpression(context, left, props, parameters, enumTypes);
        WalkExpression(context, right, props, parameters, enumTypes);
    }
}