namespace Poly.DomainModeling;

/// <summary>
/// Defines the identity (primary key) of a data type.
/// Identity properties are structurally distinguished from regular unique+required properties,
/// enabling code generators, persistence layers, and relationship resolution to reason about
/// entity references and equality semantics.
/// </summary>
/// <param name="PropertyNames">
/// The property names that form the composite identity.
/// A single-property identity is the common case; multiple properties form a composite key.
/// All named properties must exist on the owning <see cref="DataType"/>.
/// </param>
public sealed record Identity(IReadOnlyList<string> PropertyNames);