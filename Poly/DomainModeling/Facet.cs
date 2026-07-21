namespace Poly.DomainModeling;

/// <summary>
/// Base type for annotations / facets — foreign-system metadata that does
/// <b>not</b> constrain domain values. Facets are pure metadata for target
/// exporters (column mapping, JSON naming, PII labels, …).
/// </summary>
/// <remarks>
/// Unlike <see cref="Constraint"/>, facets have no validation semantics and
/// do not affect VM execution. They are stored on <see cref="DomainType.Facets"/>
/// (type-level) and <see cref="Property.Facets"/> (property-level).
/// </remarks>
public abstract record Facet : DomainObject;