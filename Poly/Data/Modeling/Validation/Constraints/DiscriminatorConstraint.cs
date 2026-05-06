namespace Poly.Data.Modeling.Validation.Constraints;

/// <summary>
/// Represents a discriminator constraint that defines a tagged-union shape on an entity.
/// One discriminator property + closed enum values + per-value variant requirements.
/// </summary>
public sealed class DiscriminatorConstraint : Constraint {
    /// <summary>
    /// The name of the discriminator property on the entity.
    /// </summary>
    public string DiscriminatorPropertyName { get; }

    /// <summary>
    /// The variants defined for this discriminator, keyed by discriminator value.
    /// </summary>
    private readonly Dictionary<string, DiscriminatorVariant> _variants;

    public DiscriminatorConstraint(string discriminatorPropertyName, IEnumerable<DiscriminatorVariant> variants) {
        ArgumentNullException.ThrowIfNull(discriminatorPropertyName);
        ArgumentNullException.ThrowIfNull(variants);

        DiscriminatorPropertyName = discriminatorPropertyName;
        _variants = variants.ToDictionary(v => v.Value, StringComparer.Ordinal);

        if (_variants.Count == 0) {
            throw new ArgumentException("Discriminator constraint requires at least one variant.", nameof(variants));
        }

        var duplicateValues = _variants
            .GroupBy(static entry => entry.Key, StringComparer.Ordinal)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .ToArray();

        if (duplicateValues.Length > 0) {
            throw new ArgumentException($"Discriminator constraint contains duplicate variant values: {string.Join(", ", duplicateValues)}.", nameof(variants));
        }
    }

    public DiscriminatorConstraint(string discriminatorPropertyName, params DiscriminatorVariant[] variants)
        : this(discriminatorPropertyName, (IEnumerable<DiscriminatorVariant>)variants) { }

    /// <summary>
    /// Gets the variant for a given discriminator value, if defined.
    /// </summary>
    public DiscriminatorVariant? GetVariant(string value) =>
        _variants.TryGetValue(value, out var variant) ? variant : null;

    /// <summary>
    /// Gets all defined variants.
    /// </summary>
    public IReadOnlyCollection<DiscriminatorVariant> Variants => _variants.Values.ToArray();

    /// <summary>
    /// Gets the set of all discriminator values defined in variants.
    /// </summary>
    public IReadOnlyCollection<string> DiscriminatorValues => _variants.Keys.ToArray();

    /// <summary>
    /// Discriminator constraint applies to entities (DomainType with entity semantics).
    /// </summary>
    public TypeCategory ApplicableCategories => TypeCategory.None;
}

/// <summary>
/// Defines the requirements for a specific discriminator value (variant).
/// </summary>
/// <param name="Value">The discriminator value that selects this variant.</param>
/// <param name="RequiredProperties">Properties required when this variant is active.</param>
/// <param name="ForbiddenProperties">Properties that must not be present when this variant is active.</param>
public sealed record DiscriminatorVariant(
    string Value,
    IReadOnlyCollection<string> RequiredProperties,
    IReadOnlyCollection<string> ForbiddenProperties) {

    public DiscriminatorVariant(string value, params string[] requiredProperties)
        : this(value, requiredProperties, Array.Empty<string>()) { }

    public DiscriminatorVariant(string value, string[] requiredProperties, string[] forbiddenProperties)
        : this(value, (IReadOnlyCollection<string>)requiredProperties, (IReadOnlyCollection<string>)forbiddenProperties) { }
}