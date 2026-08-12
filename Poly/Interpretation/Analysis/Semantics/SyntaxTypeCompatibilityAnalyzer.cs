using Poly.Analysis;

namespace Poly.Interpretation.Analysis.Semantics;

public static class SyntaxTypeCompatibilityAnalyzerExtensions {
    extension(AnalyzerBuilder builder) {
        /// <summary>Adds the <see cref="SyntaxTypeCompatibilityAnalyzer"/> to the pipeline.</summary>
        public AnalyzerBuilder UseSyntaxTypeCompatibility() {
            builder.AddAnalyzer(new SyntaxTypeCompatibilityAnalyzer());
            return builder;
        }
    }
}

/// <summary>
/// Validate pack on the lowered Syntax AST: operation type compatibility. The
/// interpretation pipeline resolved types (TypeAndMemberResolver) and classified
/// representations (ValueRepresentationAnalysis) but never rejected incompatible
/// operations — a Text member compared to a Number constant, a bool in arithmetic,
/// a non-Boolean operand to <c>not</c>. The VM then silently coerced garbage, and the
/// C# export failed at compile. This pass reports the class at VM-compile time, so the
/// runtime fails loud even for programmatically-constructed or MCP-driven expressions
/// that bypass the DSL authoring analyzer.
///
/// Compatibility is checked at the CLR-type-category level. Unknown and null operands
/// are skipped (the property bag is loosely typed; null comparisons are supported), and
/// enum operands accept string constants (runtime stores enum values as strings).
/// </summary>
internal sealed class SyntaxTypeCompatibilityAnalyzer : INodeAnalyzer {
    public const string Id = "SyntaxTypeCompatibility";
    public string PassName => Id;

    /// <summary>Diagnostic code used by <see cref="Interpreter.Compile"/> to fail loud.</summary>
    public const string DiagnosticCode = "VmTypeCompatibility";

    public string[] Dependencies => [TypeAndMemberResolver.Id, ValueRepresentationAnalyzer.Id];

    public void Analyze(AnalysisContext context, Node node) {
        if (!context.ShouldAnalyze(node))
            return;

        switch (node) {
            case Equal eq:
                CheckComparison(context, eq.LeftHandValue, eq.RightHandValue);
                break;
            case NotEqual ne:
                CheckComparison(context, ne.LeftHandValue, ne.RightHandValue);
                break;
            case LessThan lt:
                CheckComparison(context, lt.LeftHandValue, lt.RightHandValue);
                break;
            case LessThanOrEqual lte:
                CheckComparison(context, lte.LeftHandValue, lte.RightHandValue);
                break;
            case GreaterThan gt:
                CheckComparison(context, gt.LeftHandValue, gt.RightHandValue);
                break;
            case GreaterThanOrEqual gte:
                CheckComparison(context, gte.LeftHandValue, gte.RightHandValue);
                break;
            case Add add:
                CheckArithmetic(context, add.LeftHandValue, add.RightHandValue, isAdd: true);
                break;
            case Subtract sub:
                CheckArithmetic(context, sub.LeftHandValue, sub.RightHandValue, isAdd: false);
                break;
            case Multiply mul:
                CheckArithmetic(context, mul.LeftHandValue, mul.RightHandValue, isAdd: false);
                break;
            case Divide div:
                CheckArithmetic(context, div.LeftHandValue, div.RightHandValue, isAdd: false);
                break;
            case Not not:
                var operand = ClrTypeOf(context, not.Value);
                if (operand is not null && operand != typeof(bool) && CategoryOf(operand) is not (Cat.Unknown or Cat.Null))
                    Report(context, node, $"'not' requires a Boolean operand (got '{operand.Name}')");
                break;
            case And and:
                CheckBooleanOperands(context, and.LeftHandValue, and.RightHandValue);
                break;
            case Or or:
                CheckBooleanOperands(context, or.LeftHandValue, or.RightHandValue);
                break;
            case Assignment { Destination: Member dest } a:
                CheckAssign(context, dest, a.Value);
                break;
        }

        this.AnalyzeChildren(context, node);
    }

    private void CheckComparison(AnalysisContext context, Node leftNode, Node rightNode) {
        var left = ClrTypeOf(context, leftNode);
        var right = ClrTypeOf(context, rightNode);
        if (left is null || right is null) return;
        var lc = CategoryOf(left);
        var rc = CategoryOf(right);
        if (lc is Cat.Unknown or Cat.Null || rc is Cat.Unknown or Cat.Null) return;
        if (!Compatible(lc, left, rc, right))
            Report(context, leftNode,
                $"comparison between incompatible types '{left.Name}' and '{right.Name}'");
    }

    private void CheckArithmetic(AnalysisContext context, Node leftNode, Node rightNode, bool isAdd) {
        var left = ClrTypeOf(context, leftNode);
        var right = ClrTypeOf(context, rightNode);
        if (left is null || right is null) return;
        var lc = CategoryOf(left);
        var rc = CategoryOf(right);
        if (lc is Cat.Unknown or Cat.Null || rc is Cat.Unknown or Cat.Null) return;
        // numeric + numeric, or date + number (AddDays lowering), or string + string (concat)
        bool ok = (lc is Cat.Number && rc is Cat.Number)
                  || (lc is Cat.Date && rc is Cat.Number)
                  || (isAdd && lc is Cat.Text && rc is Cat.Text);
        if (!ok)
            Report(context, leftNode,
                $"arithmetic operand is not numeric (got '{left.Name}' and '{right.Name}')");
    }

    private void CheckBooleanOperands(AnalysisContext context, Node leftNode, Node rightNode) {
        var left = ClrTypeOf(context, leftNode);
        var right = ClrTypeOf(context, rightNode);
        if (left is null || right is null) return;
        var lc = CategoryOf(left);
        var rc = CategoryOf(right);
        if (lc is Cat.Unknown or Cat.Null || rc is Cat.Unknown or Cat.Null) return;
        if (lc is not Cat.Boolean || rc is not Cat.Boolean)
            Report(context, leftNode,
                $"'and'/'or' requires Boolean operands (got '{left.Name}' and '{right.Name}')");
    }

    private void CheckAssign(AnalysisContext context, Member destination, Node value) {
        var target = ClrTypeOf(context, destination);
        var rhs = ClrTypeOf(context, value);
        if (target is null || rhs is null) return;
        var tc = CategoryOf(target);
        var rc = CategoryOf(rhs);
        if (tc is Cat.Unknown or Cat.Null || rc is Cat.Unknown or Cat.Null) return;
        if (tc is Cat.Enum && rc is Cat.Text) return; // enum values stored as strings
        if (!Compatible(tc, target, rc, rhs))
            Report(context, destination,
                $"cannot assign '{rhs.Name}' to '{destination.MemberName}' (type '{target.Name}')");
    }

    private void Report(AnalysisContext context, Node node, string message) =>
        context.ReportError(node, message, DiagnosticCode);

    private enum Cat { Text, Number, Boolean, Date, Enum, Guid, Null, Unknown }

    private static Type? ClrTypeOf(AnalysisContext context, Node node) =>
        context.GetMetadata<ValueRepresentationMetadata>(node)?.ClrType;

    private static Cat CategoryOf(Type type) {
        if (type == typeof(bool)) return Cat.Boolean;
        if (type == typeof(string) || type == typeof(char)) return Cat.Text;
        if (type == typeof(DateTime) || type == typeof(DateTimeOffset)
            || type == typeof(DateOnly) || type == typeof(TimeOnly) || type == typeof(TimeSpan))
            return Cat.Date;
        if (type.IsEnum) return Cat.Enum;
        if (type == typeof(Guid)) return Cat.Guid;
        if (type.IsPrimitive || type == typeof(decimal)) return Cat.Number;
        if (type.IsValueType && Nullable.GetUnderlyingType(type) is { } underlying)
            return CategoryOf(underlying);
        return Cat.Unknown;
    }

    private static bool Compatible(Cat lc, Type left, Cat rc, Type right) {
        if (lc == rc) return true;
        if (lc is Cat.Enum && rc is Cat.Text) return true;
        if (rc is Cat.Enum && lc is Cat.Text) return true;
        if (lc is Cat.Date && rc is Cat.Date) {
            bool leftDateTime = left == typeof(DateTime) || left == typeof(DateTimeOffset);
            bool rightDateTime = right == typeof(DateTime) || right == typeof(DateTimeOffset);
            return leftDateTime == rightDateTime;
        }
        return false;
    }
}