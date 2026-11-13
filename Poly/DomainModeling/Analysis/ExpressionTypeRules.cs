using Poly.DomainModeling.Ontology;

using Add = Poly.DomainModeling.Ontology.Add;
using And = Poly.DomainModeling.Ontology.And;
using Divide = Poly.DomainModeling.Ontology.Divide;
using Multiply = Poly.DomainModeling.Ontology.Multiply;
using Not = Poly.DomainModeling.Ontology.Not;
using Or = Poly.DomainModeling.Ontology.Or;
using Subtract = Poly.DomainModeling.Ontology.Subtract;

namespace Poly.DomainModeling.Analysis;

// Type rules: how two types relate and how a type is described. Moved out of the core file unchanged.

internal sealed partial class ExpressionTypeAnalyzer : INodeAnalyzer {
    private static bool IsNumeric(TypeCategory c) => c is TypeCategory.Number;

    private static bool IsDate(TypeCategory c) => c is TypeCategory.Date;

    private static string Describe(TypeInfo t) => t.Category switch {
        TypeCategory.Text => "Text",
        TypeCategory.Number => "Number",
        TypeCategory.Boolean => "Boolean",
        TypeCategory.Date => $"Date ({t.TypeName ?? "date"})",
        TypeCategory.Time => $"Time ({t.TypeName ?? "time"})",
        TypeCategory.Duration => "duration",
        TypeCategory.Enum => $"enum {t.TypeName ?? "?"}",
        TypeCategory.Guid => "Guid",
        TypeCategory.Null => "null",
        TypeCategory.Unknown => "unknown",
        _ => "unknown",
    };

    /// <param name="assigning">
    /// When true, <paramref name="left"/> is the source and <paramref name="right"/> is the
    /// target. Catalog semantics: Date onto DateTime is a widen; DateTime onto Date is not.
    /// </param>
    private static bool Compatible(AnalysisContext context, TypeInfo left, TypeInfo right, bool assigning = false) {
        if (left.Category is TypeCategory.Unknown || right.Category is TypeCategory.Unknown)
            return true;
        if (left.Category is TypeCategory.Null)
            return right.Category is TypeCategory.Text or TypeCategory.Guid;
        if (right.Category is TypeCategory.Null)
            return left.Category is TypeCategory.Text or TypeCategory.Guid;
        if (left.Category is TypeCategory.Enum && right.Category is TypeCategory.Enum)
            return string.Equals(left.TypeName, right.TypeName, StringComparison.Ordinal);
        if (left.Category is TypeCategory.Enum && right.Category is TypeCategory.Text)
            return true;
        if (right.Category is TypeCategory.Enum && left.Category is TypeCategory.Text)
            return true;

        if (assigning) {
            var leftSemantics = SemanticsOf(context, left.TypeName);
            var rightSemantics = SemanticsOf(context, right.TypeName);
            if (leftSemantics is PrimitiveSemantics.Date && rightSemantics is PrimitiveSemantics.DateTime)
                return true;
            if (leftSemantics is PrimitiveSemantics.DateTime && rightSemantics is PrimitiveSemantics.Date)
                return false;
            if (left.Category != right.Category)
                return false;
            // Number, Int, and Int64 widen onto Decimal at the assign site only.
            if (right.TypeName is "Decimal" && IsIntegralTypeName(left.TypeName))
                return true;
            if (left.TypeName is { } ln && right.TypeName is { } rn)
                return string.Equals(CanonicalName(ln), CanonicalName(rn), StringComparison.Ordinal);
            return true;
        }

        return left.Category == right.Category
            || string.Equals(CanonicalName(left.TypeName), CanonicalName(right.TypeName), StringComparison.Ordinal);
    }

    /// <summary>
    /// A <c>decimal</c> literal combined with <c>Number</c>, <c>Int</c>, or <c>Int64</c>.
    /// A <c>Decimal</c> property combined with a <c>Number</c> property stays legal.
    /// </summary>
    private bool ReportFractionalLiteral(
        AnalysisContext context,
        DomainExpression left, TypeInfo leftType,
        DomainExpression right, TypeInfo rightType) {
        if (left is Literal { Value: decimal } leftLit && IsIntegralTypeName(rightType.TypeName)) {
            Report(context, leftLit, FractionalLiteralMessage(leftLit, rightType.TypeName!));
            return true;
        }
        if (right is Literal { Value: decimal } rightLit && IsIntegralTypeName(leftType.TypeName)) {
            Report(context, rightLit, FractionalLiteralMessage(rightLit, leftType.TypeName!));
            return true;
        }
        return false;
    }

    private static string FractionalLiteralMessage(Literal literal, string typeName) {
        var message = $"Fractional literal is not legal on {typeName}. Use Decimal.";
        if (literal.Line > 0)
            message += $" (line {literal.Line}, col {literal.Col})";
        return message;
    }

    private static bool IsIntegralTypeName(string? typeName) =>
        typeName is "Number" or "Int" or "Int64";

    private static string? CanonicalName(string? typeName) => typeName switch {
        "String" => "Text",
        "Bool" => "Boolean",
        "Int" or "Int64" => "Number",
        "Guid" => "Uuid",
        _ => typeName
    };

    private static PrimitiveSemantics? SemanticsOf(AnalysisContext context, string? typeName) {
        if (typeName is null) return null;
        var lookup = context.GetTypeLookup();
        var primitive = lookup?.Domain.Types.OfType<Ontology.PrimitiveType>().FirstOrDefault(p =>
            string.Equals(p.Name, typeName, StringComparison.Ordinal));
        return primitive?.Semantics;
    }

    private static bool MixedDateGranularity(AnalysisContext context, TypeInfo left, TypeInfo right) {
        var leftSemantics = SemanticsOf(context, left.TypeName);
        var rightSemantics = SemanticsOf(context, right.TypeName);
        return leftSemantics is PrimitiveSemantics.Date && rightSemantics is PrimitiveSemantics.DateTime
            || leftSemantics is PrimitiveSemantics.DateTime && rightSemantics is PrimitiveSemantics.Date;
    }

    private static void Report(AnalysisContext context, Node where, string message) =>
        context.ReportError(where, message, DomainModelDiagnosticCodes.SemanticTypeCompatibility);
}