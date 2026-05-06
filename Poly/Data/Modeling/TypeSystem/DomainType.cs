using Poly.Data.Modeling.Validation;
using Poly.Data.Modeling.Validation.Constraints;

namespace Poly.Data.Modeling.TypeSystem;

/// <summary>
/// Represents a type within a domain, which can be an entity or a value object.
/// </summary>
public abstract partial record DomainType : DomainMember {
    protected readonly List<Property> _properties = [];
    protected readonly List<Constraint> _constraints = [];

    public DomainType(Domain domain, string name, params IEnumerable<Property> properties) : base(domain, name) {
        _properties.AddRange(properties);
    }

    public DomainType(Domain domain, string name, IEnumerable<Constraint> constraints, params IEnumerable<Property> properties) : base(domain, name) {
        _properties.AddRange(properties);
        _constraints.AddRange(constraints);
    }

    /// <summary>
    /// Gets the properties of the type.
    /// </summary>
    public IReadOnlyCollection<Property> Properties => _properties;

    /// <summary>
    /// Constraints inherited by all properties typed to this type.
    /// The implementation platform uses these to determine the correct runtime representation.
    /// </summary>
    public IReadOnlyCollection<Constraint> Constraints => _constraints;

    /// <summary>
    /// In the domain model, nullability is constraint-driven: required types are non-nullable.
    /// </summary>
    public bool IsRequired => _constraints.Any(static c => c.IsOrContains<RequiredConstraint>());

    /// <summary>
    /// True when no required constraint is present on this type.
    /// </summary>
    public bool IsNullable => !IsRequired;
}