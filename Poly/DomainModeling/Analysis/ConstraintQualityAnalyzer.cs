using Poly.DomainModeling.Constraints;
using Poly.Syntax.Analysis;

namespace Poly.DomainModeling.Analysis;

internal sealed class ConstraintQualityAnalyzer : INodeAnalyzer {
    public const string Id = "DomainConstraintQualityAnalyzer";
    public string PassName => Id;
    public string[] Dependencies => [];
    public void Analyze(AnalysisContext context, Node node) {
        if (!context.ShouldAnalyze(node)) {
            return;
        }

        switch (node) {
            case Domain domain:
                ValidateDomainFixedPoint(context, domain);
                break;
            case Property property:
                ValidatePropertyConstraints(context, property);
                break;
        }

        this.AnalyzeChildren(context, node);
    }

    private static void ValidateDomainFixedPoint(AnalysisContext context, Domain domain) {
        if (!context.TryBeginAnalyzerVisit<ConstraintQualityAnalyzer>(domain)) {
            return;
        }

        var lookup = context.GetMetadata<DomainTypeLookupMetadata>(default);
        if (lookup is null) return;

        // Inheritance removed — no parent-entity fixed-point validation needed.
    }

    private static void ValidatePropertyConstraints(AnalysisContext context, Property property) {
        if (!context.TryBeginAnalyzerVisit<ConstraintQualityAnalyzer>(property)) {
            return;
        }

        var constraints = property.Constraints;
        ValidateRangeSatisfiability(context, property, constraints);
        ValidateLengthSatisfiability(context, property, constraints);
        ValidateEnumCombination(context, property, constraints);
        ValidateConstraintTypeCompatibility(context, property, constraints);
    }

    private static void ValidateEntityFixedPoint(AnalysisContext context, Entity entity, Entity parentEntity) {
        foreach (var property in entity.Properties) {
            var parentProperty = parentEntity.Properties
                .FirstOrDefault(p => string.Equals(p.Name, property.Name, StringComparison.Ordinal));
            if (parentProperty is null) continue;

            var parentRequired = parentProperty.Constraints.Any(static c => c is RequiredConstraint);
            var childRequired = property.Constraints.Any(static c => c is RequiredConstraint);
            if (parentRequired && !childRequired) {
                context.ReportWarning(
                    property,
                    $"Property '{property.Name}' on '{entity.Name}' breaks constraint fixed-point by weakening parent requiredness.",
                    DomainModelDiagnosticCodes.ConstraintFixedPoint);
            }

            if (!string.Equals(property.Type.TypeName, parentProperty.Type.TypeName, StringComparison.Ordinal)) {
                context.ReportWarning(
                    property,
                    $"Property '{property.Name}' on '{entity.Name}' overrides parent type '{parentProperty.Type.TypeName}' with incompatible type '{property.Type.TypeName}'.",
                    DomainModelDiagnosticCodes.ConstraintFixedPoint);
            }
        }
    }

    private static void ValidateRangeSatisfiability(
        AnalysisContext context, Property property, IReadOnlyList<Constraint> constraints) {
        foreach (var range in constraints.OfType<RangeConstraint>()) {
            if (range.Minimum is not null && range.Maximum is not null && Compare(range.Minimum, range.Maximum) > 0) {
                context.ReportError(
                    property,
                    $"Property '{property.Name}' has unsatisfiable RangeConstraint: minimum exceeds maximum.",
                    DomainModelDiagnosticCodes.ConstraintSatisfiability);
            }
        }
    }

    private static void ValidateLengthSatisfiability(
        AnalysisContext context, Property property, IReadOnlyList<Constraint> constraints) {
        foreach (var length in constraints.OfType<LengthConstraint>()) {
            if (length.MinLength < 0 || length.MaxLength < 0 || length.MinLength > length.MaxLength) {
                context.ReportError(
                    property,
                    $"Property '{property.Name}' has unsatisfiable LengthConstraint bounds (min={length.MinLength}, max={length.MaxLength}).",
                    DomainModelDiagnosticCodes.ConstraintSatisfiability);
            }
        }
    }

    private static void ValidateEnumCombination(
        AnalysisContext context, Property property, IReadOnlyList<Constraint> constraints) {
        var enumConstraint = constraints.OfType<EnumConstraint>().LastOrDefault();
        if (enumConstraint is null) {
            return;
        }

        foreach (var range in constraints.OfType<RangeConstraint>()) {
            if (enumConstraint.Members.Any(m => m.EffectiveCanonicalValue is null || !IsWithinRange(m.EffectiveCanonicalValue, range))) {
                context.ReportError(
                    property,
                    $"Property '{property.Name}' has unsatisfiable EnumConstraint + RangeConstraint combination.",
                    DomainModelDiagnosticCodes.ConstraintSatisfiability);
                break;
            }
        }

        foreach (var length in constraints.OfType<LengthConstraint>()) {
            if (enumConstraint.Members.Any(m => m.EffectiveCanonicalValue is not string text || !IsWithinLength(text, length))) {
                context.ReportError(
                    property,
                    $"Property '{property.Name}' has unsatisfiable EnumConstraint + LengthConstraint combination.",
                    DomainModelDiagnosticCodes.ConstraintSatisfiability);
                break;
            }
        }
    }

    private static bool IsWithinRange(object value, RangeConstraint range) {
        if (range.Minimum is not null && Compare(value, range.Minimum) < 0) {
            return false;
        }
        if (range.Maximum is not null && Compare(value, range.Maximum) > 0) {
            return false;
        }
        return true;
    }

    private static bool IsWithinLength(string value, LengthConstraint length) {
        if (value.Length < length.MinLength) {
            return false;
        }
        if (value.Length > length.MaxLength) {
            return false;
        }
        return true;
    }

    private static int Compare(object left, object right) {
        if (left is IComparable comparable && AreComparableTypes(left.GetType(), right.GetType())) {
            return comparable.CompareTo(Convert.ChangeType(right, left.GetType()));
        }
        return 0;
    }

    private static bool AreComparableTypes(Type left, Type right) {
        if (left == right) {
            return true;
        }
        return IsNumericType(left) && IsNumericType(right);
    }

    private static bool IsNumericType(Type type) =>
        Type.GetTypeCode(type) is TypeCode.Byte
            or TypeCode.SByte
            or TypeCode.Int16
            or TypeCode.UInt16
            or TypeCode.Int32
            or TypeCode.UInt32
            or TypeCode.Int64
            or TypeCode.UInt64
            or TypeCode.Single
            or TypeCode.Double
            or TypeCode.Decimal;

    private static void ValidateConstraintTypeCompatibility(
        AnalysisContext context, Property property, IReadOnlyList<Constraint> constraints) {
        var hasRange = constraints.OfType<RangeConstraint>().Any();
        var hasLength = constraints.OfType<LengthConstraint>().Any();

        if (!hasRange && !hasLength) return;

        var resolved = context.GetMetadata<ResolvedTypeReferenceMetadata>(property.Type);
        if (resolved?.Type is not PrimitiveType primitiveType) return;

        var category = primitiveType.TypeCategory;

        if (hasRange && !category.Is(TypeCategory.Numeric)) {
            context.ReportError(
                property,
                $"Property '{property.Name}' has a RangeConstraint but its type '{property.Type.TypeName}' does not resolve to a numeric type.",
                DomainModelDiagnosticCodes.SemanticTypeCompatibility);
        }

        if (hasLength && !category.Is(TypeCategory.Text)) {
            context.ReportError(
                property,
                $"Property '{property.Name}' has a LengthConstraint but its type '{property.Type.TypeName}' does not resolve to a text type.",
                DomainModelDiagnosticCodes.SemanticTypeCompatibility);
        }
    }
}