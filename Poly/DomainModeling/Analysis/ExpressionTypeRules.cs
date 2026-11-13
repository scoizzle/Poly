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
    /// <see cref="Compatible"/>, <c>Unifies</c> and the arithmetic arms. Numeric family = Int32, Int64, Float32,
    /// Float64 and Decimal; date family = DateOnly and DateTime; an entity, a value type and Binary are families of
    /// their own, compared by name. A type with no definition has no family (<see cref="Family"/> is null): it is
    /// never compatible with anything, and the consuming site records it for DMSEM006 (T-05b).
    /// </summary>
    private enum TypeFamily { Null, Text, Number, Boolean, Date, Time, Duration, Enum, Guid, Entity, Value, Binary }

    private static TypeFamily? Family(ExprType type) {
        if (type.IsNullLiteral) return TypeFamily.Null;
        if (type.Definition is not { } definition) return null;
        if (definition.Kind is TypeKind.Enum) return TypeFamily.Enum;
        if (definition.PrimitiveType is null) {
            return definition.Kind switch {
                TypeKind.Class => TypeFamily.Entity,
                TypeKind.Struct => TypeFamily.Value,
                _ => null,
            };
        }
        return definition.PrimitiveType switch {
            Prim.String => TypeFamily.Text,
            Prim.Int32 or Prim.Int64 or Prim.Float32 or Prim.Float64 or Prim.Decimal => TypeFamily.Number,
            Prim.Boolean => TypeFamily.Boolean,
            Prim.DateOnly or Prim.DateTime => TypeFamily.Date,
            Prim.TimeOnly => TypeFamily.Time,
            Prim.TimeSpan => TypeFamily.Duration,
            Prim.Guid => TypeFamily.Guid,
            Prim.ByteArray => TypeFamily.Binary,
            _ => null,
        };
    }

    /// <summary>True when the type has a family; false for an expression the analyzer could not type.</summary>
    private static bool IsTyped(ExprType type) => Family(type) is not null;

    private static bool IsNumeric(ExprType type) => Family(type) is TypeFamily.Number;

    private static bool IsDate(ExprType type) => Family(type) is TypeFamily.Date;

    private static bool IsDateOnly(ExprType type) => type.Definition?.PrimitiveType is Prim.DateOnly;

    private static bool IsDateTime(ExprType type) => type.Definition?.PrimitiveType is Prim.DateTime;

    /// <summary>A type as a message names it. Only typed values reach a message: an untyped one is DMSEM006's.</summary>
    private static string Describe(ExprType t) => Family(t) switch {
        TypeFamily.Text => "Text",
        // Only Decimal is distinct (#581); Float and Int32 keep the family name (removing row T-08).
        TypeFamily.Number => t.Definition?.PrimitiveType is Prim.Decimal ? "Decimal" : "Number",
        TypeFamily.Boolean => "Boolean",
        TypeFamily.Date => $"Date ({t.DomainName ?? "date"})",
        TypeFamily.Time => $"Time ({t.DomainName ?? "time"})",
        TypeFamily.Duration => "duration",
        TypeFamily.Enum => $"enum {t.DomainName ?? "?"}",
        TypeFamily.Guid => "Guid",
        TypeFamily.Null => "null",
        TypeFamily.Entity => $"entity {t.DomainName ?? "?"}",
        TypeFamily.Value => $"value {t.DomainName ?? "?"}",
        TypeFamily.Binary => "Binary",
        null => t.DomainName ?? "?",
        _ => throw new System.Diagnostics.UnreachableException($"No description for type family '{Family(t)}'."),
    };

    /// <param name="assigning">
    /// When true, <paramref name="left"/> is the source and <paramref name="right"/> is the
    /// target. Catalog semantics: Date onto DateTime is a widen; DateTime onto Date is not.
    /// </param>
    private static bool Compatible(ExprType left, ExprType right, bool assigning = false) {
        if (Family(left) is not { } leftFamily || Family(right) is not { } rightFamily)
            return false;
        // N1 (removing row T-06): null against an entity, a value type or Binary stays accepted.
        if (leftFamily is TypeFamily.Null)
            return rightFamily is TypeFamily.Text or TypeFamily.Guid or TypeFamily.Entity or TypeFamily.Value or TypeFamily.Binary;
        if (rightFamily is TypeFamily.Null)
            return leftFamily is TypeFamily.Text or TypeFamily.Guid or TypeFamily.Entity or TypeFamily.Value or TypeFamily.Binary;
        if (leftFamily is TypeFamily.Entity or TypeFamily.Value && leftFamily == rightFamily)
            return string.Equals(left.DomainName, right.DomainName, StringComparison.Ordinal);
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
            ? $"Integer literal {value.ToString(System.Globalization.CultureInfo.InvariantCulture)} is out of range for Number (64-bit)"
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

    // ── untyped sites (T-05b, A1): stamped here, reported by UntypedExpressionPass ──

    /// <summary>
    /// Records that <paramref name="consumer"/> needed the type of <paramref name="expr"/> and the analyzer has none.
    /// Nothing is reported here: <see cref="UntypedExpressionPass"/> reports DMSEM006 after the passes that explain an
    /// expression with a more specific error have run, and only when none did (A1, A2).
    /// </summary>
    private void RequireTyped(
        AnalysisContext context, DomainExpression expr, ExprType type, Node consumer,
        Dictionary<string, string>? props, Dictionary<string, string>? parameters, Dictionary<string, EnumType> enumTypes) {
        var typeName = type is { Definition: null, DomainName: { } name } ? name : null;
        if (context.GetTypeLookup()?.Domain is not { } domain) {
            // No domain, so no type model and no closing pass: fail closed here.
            context.ReportError(expr, $"cannot determine the type of '{PrintedText(context, expr)}': the analysis has no domain type model",
                DomainModelDiagnosticCodes.SemanticUntypedExpression);
            return;
        }
        var reason = typeName is not null
            ? $"its type '{typeName}' {Unmodeled(domain, typeName)}"
            : UntypedReason(context, expr, props, parameters, enumTypes);
        context.GetOrAddMetadata(domain, () => new UntypedSitesMetadata()).Add(new UntypedSite(
            expr, consumer, typeName, $"cannot determine the type of '{PrintedText(context, expr)}': {reason}"));
    }

    /// <summary>Records that <paramref name="consumer"/>'s declared type <paramref name="typeName"/> has no definition.</summary>
    private static void RequireTypedTarget(AnalysisContext context, Node consumer, string typeName) {
        if (context.GetTypeLookup()?.Domain is not { } domain) {
            // No domain, so no type model and no closing pass: fail closed here.
            context.ReportError(consumer, $"cannot determine the type '{typeName}': the analysis has no domain type model",
                DomainModelDiagnosticCodes.SemanticUntypedExpression);
            return;
        }
        context.GetOrAddMetadata(domain, () => new UntypedSitesMetadata()).Add(new UntypedSite(
            null, consumer, typeName, $"cannot determine the type '{typeName}': it {Unmodeled(domain, typeName)}"));
    }

    private static string Unmodeled(Domain domain, string typeName) {
        var clr = RuntimeAnalysisCache.Session(domain).TypeMaps.ToClrTypeName(typeName);
        return string.Equals(clr, typeName, StringComparison.Ordinal)
            ? "cannot be modeled: no declared type or primitive mapping defines it"
            : $"cannot be modeled: it maps to the CLR type '{clr}', which the type model has no primitive for";
    }

    /// <summary>Why the analyzer has no type for <paramref name="expr"/>, in the words of the form the user wrote.</summary>
    private string UntypedReason(
        AnalysisContext context, DomainExpression expr,
        Dictionary<string, string>? props, Dictionary<string, string>? parameters, Dictionary<string, EnumType> enumTypes) {
        var noEntity = props is not EntityScope;
        switch (expr) {
            case PropertyAccess { Name: var name }:
                var enums = EnumsDeclaring(enumTypes, name);
                return enums.Count > 1
                    ? $"'{name}' is a member of more than one enum ({string.Join(", ", enums)}); compare it with a property of one of them"
                    : $"'{name}' is not a property, parameter or enum member here";
            case ParameterAccess { Name: var name }:
                return $"'{name}' is not a declared parameter";
            case RelationshipNavigation or OwnedAccess when noEntity:
            case AnyExpr or AllExpr or NoneExpr or CountExpr or SumExpr when noEntity:
                return "there is no entity here to read a relationship from (a function body has only its parameters)";
            case RelationshipNavigation:
                return "the path does not reach a property through to-one relationships";
            case OwnedAccess:
                return "the owned member does not resolve";
            case SumExpr:
            case AnyExpr or AllExpr or NoneExpr or CountExpr:
                return "its relationship does not resolve to an entity";
            case Add or Subtract or Multiply or Divide:
                var operands = expr.Children.OfType<DomainExpression>()
                    .Select(o => InferType(context, o, props, parameters, enumTypes)).ToList();
                return operands.All(IsTyped)
                    ? $"no arithmetic operator is defined for '{Describe(operands[0])}' and '{Describe(operands[1])}'"
                    : "an operand has no type";
            case MatchExpr:
                return "an arm value has no type";
            case FunctionCall call:
                return FunctionAnalysis.Find(context, call.Name) is null
                    ? $"'{call.Name}' is not a declared function with a return type"
                    : "its return type cannot be modeled";
            default:
                return "the analyzer has no type rule for this form";
        }
    }

    private static void Report(AnalysisContext context, Node where, string message) =>
        context.ReportError(where, message, DomainModelDiagnosticCodes.SemanticTypeCompatibility);
}