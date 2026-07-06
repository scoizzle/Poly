using Poly.Data.Modeling.TypeSystem;
using Poly.Data.Modeling.Validation;
using Poly.Data.Modeling.Validation.Constraints;

namespace Poly.Data.Modeling;

internal sealed class ConstraintQualityAnalyzer : INodeAnalyzer {
    public const string Id = "DataConstraintQualityAnalyzer";
    public string PassName => Id;
    public string[] Dependencies => [];
    public void Analyze(AnalysisContext context, Node node) {
        if (!context.ShouldAnalyze(node)) {
            return;
        }

        switch (node) {
            case Entity entity:
                AnalyzeEntity(context, entity);
                break;
            case Property property:
                AnalyzeProperty(context, property);
                break;
        }

        this.AnalyzeChildren(context, node);
    }

    private static void AnalyzeEntity(AnalysisContext context, Entity entity) {
        if (!context.TryBeginAnalyzerVisit<ConstraintQualityAnalyzer>(entity)) {
            return;
        }

        ValidateConstraintFixedPoint(context, entity);
    }

    private static void AnalyzeProperty(AnalysisContext context, Property property) {
        if (!context.TryBeginAnalyzerVisit<ConstraintQualityAnalyzer>(property)) {
            return;
        }

        ValidateConstraintSatisfiability(context, property, property.EffectiveConstraints);
    }

    private static void ValidateConstraintFixedPoint(AnalysisContext context, Entity entity) {
        if (entity.ParentEntity is null) {
            return;
        }

        foreach (var property in entity.Properties) {
            var parentProperty = entity.ParentEntity.Properties
                .FirstOrDefault(p => string.Equals(p.Name, property.Name, StringComparison.Ordinal));
            if (parentProperty is null) {
                continue;
            }

            var parentRequired = parentProperty.EffectiveConstraints.Any(static c => c.IsOrContains<RequiredConstraint>());
            var childRequired = property.EffectiveConstraints.Any(static c => c.IsOrContains<RequiredConstraint>());
            if (parentRequired && !childRequired) {
                context.ReportWarning(
                    property,
                    $"Property '{property.Name}' on '{entity.Name}' breaks constraint fixed-point by weakening parent requiredness.",
                    DomainModelDiagnosticCodes.ConstraintFixedPoint);
            }

            if (!DomainTypeAssignability.CanAssign(parentProperty.Type, property.Type)
                && !DomainTypeAssignability.CanAssign(property.Type, parentProperty.Type)) {
                context.ReportWarning(
                    property,
                    $"Property '{property.Name}' on '{entity.Name}' breaks constraint fixed-point with incompatible override type '{property.Type.Name}' from parent type '{parentProperty.Type.Name}'.",
                    DomainModelDiagnosticCodes.ConstraintFixedPoint);
            }
        }
    }

    private static void ValidateConstraintSatisfiability(AnalysisContext context, Property property, IReadOnlyCollection<Constraint> constraints) {
        foreach (var range in constraints.OfType<RangeConstraint>()) {
            if (range.MinValue is not null && range.MaxValue is not null && Compare(range.MinValue, range.MaxValue) > 0) {
                context.ReportError(
                    property,
                    $"Property '{property.Name}' has unsatisfiable range constraint: min exceeds max.",
                    DomainModelDiagnosticCodes.ConstraintSatisfiability);
            }
        }

        foreach (var length in constraints.OfType<LengthConstraint>()) {
            if (length.MinLength is < 0 || length.MaxLength is < 0 || (length.MinLength is not null && length.MaxLength is not null && length.MinLength > length.MaxLength)) {
                context.ReportError(
                    property,
                    $"Property '{property.Name}' has unsatisfiable length constraint bounds.",
                    DomainModelDiagnosticCodes.ConstraintSatisfiability);
            }
        }

        var enumConstraint = constraints.OfType<EnumConstraint>().LastOrDefault();
        if (enumConstraint is null) {
            return;
        }

        foreach (var range in constraints.OfType<RangeConstraint>()) {
            if (enumConstraint.Members.Select(static member => member.EffectiveCanonicalValue).Any(value => value is null || !IsWithinRange(value, range))) {
                context.ReportError(
                    property,
                    $"Property '{property.Name}' has unsatisfiable EnumConstraint + RangeConstraint combination.",
                    DomainModelDiagnosticCodes.ConstraintSatisfiability);
                break;
            }
        }

        foreach (var length in constraints.OfType<LengthConstraint>()) {
            if (enumConstraint.Members.Select(static member => member.EffectiveCanonicalValue).Any(value => value is not string text || !IsWithinLength(text, length))) {
                context.ReportError(
                    property,
                    $"Property '{property.Name}' has unsatisfiable EnumConstraint + LengthConstraint combination.",
                    DomainModelDiagnosticCodes.ConstraintSatisfiability);
                break;
            }
        }
    }

    private static int Compare(object left, object right) {
        if (left is IComparable comparable && AreComparableTypes(left.GetType(), right.GetType())) {
            return comparable.CompareTo(ConvertToType(right, left.GetType()));
        }

        return 0;
    }

    private static bool IsWithinRange(object value, RangeConstraint range) {
        if (range.MinValue is not null && Compare(value, range.MinValue) < 0) {
            return false;
        }

        if (range.MaxValue is not null && Compare(value, range.MaxValue) > 0) {
            return false;
        }

        return true;
    }

    private static bool IsWithinLength(string value, LengthConstraint length) {
        if (length.MinLength is not null && value.Length < length.MinLength.Value) {
            return false;
        }

        if (length.MaxLength is not null && value.Length > length.MaxLength.Value) {
            return false;
        }

        return true;
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

    private static object ConvertToType(object value, Type targetType) =>
        Convert.ChangeType(value, targetType);

}