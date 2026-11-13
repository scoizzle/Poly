using Poly.DomainModeling.Meaning;
using Poly.DomainModeling.Ontology;

using Add = Poly.DomainModeling.Ontology.Add;
using And = Poly.DomainModeling.Ontology.And;
using Divide = Poly.DomainModeling.Ontology.Divide;
using Multiply = Poly.DomainModeling.Ontology.Multiply;
using Not = Poly.DomainModeling.Ontology.Not;
using Or = Poly.DomainModeling.Ontology.Or;
using Prim = Poly.Introspection.PrimitiveType;
using Subtract = Poly.DomainModeling.Ontology.Subtract;
using TypeKind = Poly.Introspection.TypeKind;

namespace Poly.DomainModeling.Analysis;

// Type rules: how two types relate and how a type is described, over ExprType.

internal sealed partial class ExpressionTypeAnalyzer : INodeAnalyzer {
    /// <summary>
    /// The one family key: a pure function of an <see cref="ExprType"/>'s definition (never stored), serving
    /// <see cref="Compatible"/>, <c>Unifies</c> and the arithmetic arms. Numeric family = Int32, Int64, Float64
    /// and Decimal; date family = DateOnly and DateTime. <see cref="Unknown"/> is every type the rules do not
    /// check: unresolved, named-unresolved, entity, value type and Binary. T-05 shrinks it.
    /// </summary>
    private enum TypeFamily { Unknown, Null, Text, Number, Boolean, Date, Time, Duration, Enum, Guid }

    private static TypeFamily Family(ExprType type) {
        if (type.IsNullLiteral) return TypeFamily.Null;
        if (type.Definition is not { } definition) return TypeFamily.Unknown;
        if (definition.Kind is TypeKind.Enum) return TypeFamily.Enum;
        return definition.PrimitiveType switch {
            Prim.String => TypeFamily.Text,
            Prim.Int32 or Prim.Int64 or Prim.Float64 or Prim.Decimal => TypeFamily.Number,
            Prim.Boolean => TypeFamily.Boolean,
            Prim.DateOnly or Prim.DateTime => TypeFamily.Date,
            Prim.TimeOnly => TypeFamily.Time,
            Prim.TimeSpan => TypeFamily.Duration,
            Prim.Guid => TypeFamily.Guid,
            _ => TypeFamily.Unknown,
        };
    }

    /// <summary>True for every type the rules skip (the old Unknown category).</summary>
    private static bool IsLenient(ExprType type) => Family(type) is TypeFamily.Unknown;

    private static bool IsNumeric(ExprType type) => Family(type) is TypeFamily.Number;

    private static bool IsDate(ExprType type) => Family(type) is TypeFamily.Date;

    private static bool IsDateOnly(ExprType type) => type.Definition?.PrimitiveType is Prim.DateOnly;

    private static bool IsDateTime(ExprType type) => type.Definition?.PrimitiveType is Prim.DateTime;

    private static string Describe(ExprType t) => Family(t) switch {
        TypeFamily.Text => "Text",
        // Only Decimal is distinct (#581); Float and Int32 keep the family name.
        TypeFamily.Number => t.Definition?.PrimitiveType is Prim.Decimal ? "Decimal" : "Number",
        TypeFamily.Boolean => "Boolean",
        TypeFamily.Date => $"Date ({t.DomainName ?? "date"})",
        TypeFamily.Time => $"Time ({t.DomainName ?? "time"})",
        TypeFamily.Duration => "duration",
        TypeFamily.Enum => $"enum {t.DomainName ?? "?"}",
        TypeFamily.Guid => "Guid",
        TypeFamily.Null => "null",
        _ => "unknown",
    };

    /// <param name="assigning">
    /// When true, <paramref name="left"/> is the source and <paramref name="right"/> is the
    /// target. Catalog semantics: Date onto DateTime is a widen; DateTime onto Date is not.
    /// </param>
    private static bool Compatible(ExprType left, ExprType right, bool assigning = false) {
        var leftFamily = Family(left);
        var rightFamily = Family(right);
        if (leftFamily is TypeFamily.Unknown || rightFamily is TypeFamily.Unknown)
            return true;
        if (leftFamily is TypeFamily.Null)
            return rightFamily is TypeFamily.Text or TypeFamily.Guid;
        if (rightFamily is TypeFamily.Null)
            return leftFamily is TypeFamily.Text or TypeFamily.Guid;
        if (leftFamily is TypeFamily.Enum && rightFamily is TypeFamily.Enum)
            return string.Equals(left.DomainName, right.DomainName, StringComparison.Ordinal);
        if (leftFamily is TypeFamily.Enum && rightFamily is TypeFamily.Text)
            return true;
        if (rightFamily is TypeFamily.Enum && leftFamily is TypeFamily.Text)
            return true;

        if (assigning) {
            if (IsDateOnly(left) && IsDateTime(right))
                return true;
            if (IsDateTime(left) && IsDateOnly(right))
                return false;
            if (leftFamily != rightFamily)
                return false;
            // Number, Int, and Int64 widen onto Decimal at the assign site only.
            if (right.DomainName is "Decimal" && IsIntegralTypeName(left.DomainName))
                return true;
            if (left.DomainName is { } ln && right.DomainName is { } rn)
                return string.Equals(CanonicalName(ln), CanonicalName(rn), StringComparison.Ordinal);
            return true;
        }

        return leftFamily == rightFamily
            || string.Equals(CanonicalName(left.DomainName), CanonicalName(right.DomainName), StringComparison.Ordinal);
    }

    /// <summary>
    /// A <c>decimal</c> literal combined with <c>Number</c>, <c>Int</c>, or <c>Int64</c>.
    /// A <c>Decimal</c> property combined with a <c>Number</c> property stays legal.
    /// </summary>
    private bool ReportFractionalLiteral(
        AnalysisContext context,
        DomainExpression left, ExprType leftType,
        DomainExpression right, ExprType rightType) {
        if (left is Literal { Value: decimal } leftLit && IsIntegralTypeName(rightType.DomainName)) {
            Report(context, leftLit, FractionalLiteralMessage(leftLit, rightType.DomainName!));
            return true;
        }
        if (right is Literal { Value: decimal } rightLit && IsIntegralTypeName(leftType.DomainName)) {
            Report(context, rightLit, FractionalLiteralMessage(rightLit, leftType.DomainName!));
            return true;
        }
        return false;
    }

    private static string FractionalLiteralMessage(Literal literal, string typeName) {
        // An integer-valued literal beyond the 64-bit range (9223372036854775808, 5.0e19) is out of range,
        // not fractional (#515). 5.0 and 1.5 stay fractional: the parser keeps no spelling.
        var message = literal.Value is decimal value && decimal.Truncate(value) == value
            && (value > long.MaxValue || value < long.MinValue)
            ? $"Integer literal {value} is out of range for Number (64-bit)"
            : $"Fractional literal is not legal on {typeName}. Use Decimal.";
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

    private static bool MixedDateGranularity(ExprType left, ExprType right) =>
        IsDateOnly(left) && IsDateTime(right) || IsDateTime(left) && IsDateOnly(right);

    private static void Report(AnalysisContext context, Node where, string message) =>
        context.ReportError(where, message, DomainModelDiagnosticCodes.SemanticTypeCompatibility);
}
