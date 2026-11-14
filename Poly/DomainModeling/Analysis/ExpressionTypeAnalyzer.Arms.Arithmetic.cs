using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Analysis;

// Arithmetic arms: CheckArithmetic (Add/Subtract) and CheckNumericArithmetic (Multiply/Divide); the Date - Date case stays inline in core's WalkExpression case Subtract.

internal sealed partial class ExpressionTypeAnalyzer : INodeAnalyzer {
    private void CheckArithmetic(
        AnalysisContext context, DomainExpression node, DomainExpression left, DomainExpression right,
        Dictionary<string, string> props, Dictionary<string, string>? parameters,
        Dictionary<string, EnumType> enumTypes,
        bool allowTextConcat = false) {
        var leftType = InferType(context, left, props, parameters, enumTypes);
        var rightType = InferType(context, right, props, parameters, enumTypes);
        ReportFractionalLiteral(context, left, leftType, right, rightType);
        // numeric + numeric, text + text (concatenation; `+` only), date + number (AddDays
        // lowering), or date + duration
        // (a parsed `N days` offset with a temporal left operand). An operand the analyzer cannot
        // type is recorded for DMSEM006 (T-05b). A duration without a temporal left operand
        // (Number + days, days + date) is an unresolved temporal specialization — reject,
        // never a silent numeric constant.
        RequireTypedOperands(context, node, left, leftType, right, rightType, props, parameters, enumTypes);
        if (IsTyped(leftType) && IsTyped(rightType)
            && !(IsNumeric(leftType) && IsNumeric(rightType))
            && !(allowTextConcat && Family(leftType) is TypeFamily.Text && Family(rightType) is TypeFamily.Text)
            && !(IsDate(leftType) && IsNumeric(rightType))
            && !(IsDate(leftType) && Family(rightType) is TypeFamily.Duration))
            Report(context, left,
                $"arithmetic operand is not numeric (got '{Describe(leftType)}' and '{Describe(rightType)}')");
        RejectAbsentMatchOperand(context, left, leftType);
        RejectAbsentMatchOperand(context, right, rightType);
        WalkExpression(context, left, props, parameters, enumTypes);
        WalkExpression(context, right, props, parameters, enumTypes);
    }

    private void CheckNumericArithmetic(
        AnalysisContext context, DomainExpression node, DomainExpression left, DomainExpression right,
        Dictionary<string, string> props, Dictionary<string, string>? parameters,
        Dictionary<string, EnumType> enumTypes) {
        var leftType = InferType(context, left, props, parameters, enumTypes);
        var rightType = InferType(context, right, props, parameters, enumTypes);
        ReportFractionalLiteral(context, left, leftType, right, rightType);
        RequireTypedOperands(context, node, left, leftType, right, rightType, props, parameters, enumTypes);
        if (IsTyped(leftType) && IsTyped(rightType)
            && (!IsNumeric(leftType) || !IsNumeric(rightType)))
            Report(context, left,
                $"arithmetic operand is not numeric (got '{Describe(leftType)}' and '{Describe(rightType)}')");
        RejectAbsentMatchOperand(context, left, leftType);
        RejectAbsentMatchOperand(context, right, rightType);
        WalkExpression(context, left, props, parameters, enumTypes);
        WalkExpression(context, right, props, parameters, enumTypes);
    }

    /// <summary>
    /// Arithmetic over a <c>match</c> that may be absent (an arm is <c>null</c> or optional) is not defined (T-09a): the
    /// operand has no value on one path, and no host adds to it (the VM and <c>polyc</c> refuse it, the C# compiler
    /// rejects <c>long?</c> into a <c>long</c>). Compare the match first, or give every arm a value.
    /// </summary>
    private void RejectAbsentMatchOperand(AnalysisContext context, DomainExpression operand, ExprType type) {
        if (type.Optional && IsTyped(type) && RewriteLibraryIdent(operand) is MatchExpr match)
            Report(context, match,
                "arithmetic over a 'match' that may be absent is not defined (an arm is null or optional)"
                + (match.Line > 0 ? $" (line {match.Line}, col {match.Col})" : "")
                + ": give every arm a value, or compare the match first");
    }

    private void RequireTypedOperands(
        AnalysisContext context, DomainExpression node,
        DomainExpression left, ExprType leftType, DomainExpression right, ExprType rightType,
        Dictionary<string, string> props, Dictionary<string, string>? parameters, Dictionary<string, EnumType> enumTypes) {
        if (!IsTyped(leftType)) RequireTyped(context, left, leftType, node, props, parameters, enumTypes);
        if (!IsTyped(rightType)) RequireTyped(context, right, rightType, node, props, parameters, enumTypes);
    }
}