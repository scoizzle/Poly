namespace Poly.DomainModeling.Ontology.Constraints;

/// <summary>
/// Requiredness is the default; <see cref="OptionalConstraint"/> is the explicit opt-out.
/// These predicates are the single source of the requiredness notion <em>as it applies to
/// the value</em>: a required-valued property is non-null (<c>T</c>, <c>[Required]</c>,
/// <c>NOT NULL</c>) and only an <c>optional</c> property may be absent (<c>T?</c>).
/// This is the <em>nullability</em> axis. It is not the create-presence axis — presence
/// additionally exempts <c>default(...)</c>, entry-assigned properties, and the auto-wired
/// back-reference, and it lives in <c>DMEFF011</c> (<c>EffectAnalyzer</c>).
/// </summary>
public static class PropertyConstraintExtensions {
    /// <summary>True when the property opted out of requiredness with <c>optional</c>.</summary>
    public static bool IsOptional(this IReadOnlyList<Constraint> constraints) =>
        constraints.Any(static c => c is OptionalConstraint);

    /// <inheritdoc cref="IsOptional(IReadOnlyList{Constraint})"/>
    public static bool IsOptional(this Property property) => property.Constraints.IsOptional();

    /// <summary>True when the property's value is required (non-null): not <c>optional</c>.</summary>
    public static bool IsRequiredValue(this IReadOnlyList<Constraint> constraints) =>
        !constraints.IsOptional();

    /// <inheritdoc cref="IsRequiredValue(IReadOnlyList{Constraint})"/>
    public static bool IsRequiredValue(this Property property) => !property.Constraints.IsOptional();
}