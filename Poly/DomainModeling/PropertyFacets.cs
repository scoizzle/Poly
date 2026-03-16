namespace Poly.DomainModeling;

/// <summary>
/// Whether a property value must be provided.
/// This is distinct from type-level nullability: a nullable property can still be required
/// (meaning you must explicitly provide a value, even if that value is null).
/// </summary>
public enum PropertyRequirement {
    /// <summary>The property value may be omitted (default).</summary>
    Optional,

    /// <summary>The property value must be provided.</summary>
    Required
}

/// <summary>
/// How a property may be accessed.
/// </summary>
public enum PropertyAccessibility {
    /// <summary>The property can be read and written (default).</summary>
    ReadWrite,

    /// <summary>The property can be read but not written after creation.</summary>
    ReadOnly,

    /// <summary>The property can be written but is not exposed for reading.</summary>
    WriteOnly,

    /// <summary>The property is not accessible in this context (e.g., hidden in a particular state).</summary>
    Hidden
}

/// <summary>
/// Describes the behavioral facets of a property — requirement and accessibility.
/// These are orthogonal to the property's type: a string property might be ReadOnly in one state
/// and ReadWrite in another, Required in one state and Optional in another.
/// </summary>
/// <param name="Requirement">Whether the property must be provided. Defaults to <see cref="PropertyRequirement.Optional"/>.</param>
/// <param name="Accessibility">How the property can be accessed. Defaults to <see cref="PropertyAccessibility.ReadWrite"/>.</param>
public sealed record PropertyFacets(
    PropertyRequirement Requirement = PropertyRequirement.Optional,
    PropertyAccessibility Accessibility = PropertyAccessibility.ReadWrite
);

/// <summary>
/// Overrides a property's <see cref="PropertyFacets"/> when the owning entity is in a specific
/// <see cref="LifecycleState"/>. This is how the same property can be Required+ReadOnly in one state
/// and Optional+Hidden in another, without changing the property set.
/// </summary>
/// <param name="StateName">
/// The <see cref="LifecycleState.Name"/> this override applies to.
/// Must match a state defined in the owning type's <see cref="Lifecycle"/>.
/// </param>
/// <param name="Facets">The facets that apply when the entity is in the named state.</param>
public sealed record StateFacetOverride(
    string StateName,
    PropertyFacets Facets
);