namespace Poly.Introspection;

/// <summary>
/// Represents a member of a type (field, property, or method) in the introspection system.
/// Implementations should be immutable and safe for concurrent reads.
/// </summary>
public interface ITypeMember {
    /// <summary>
    /// Gets the member name. For indexers, this is typically "Item".
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the type of the member (field/property type or method return type).
    /// </summary>
    ITypeDefinition MemberTypeDefinition { get; }

    /// <summary>
    /// Gets the type that declares this member.
    /// </summary>
    ITypeDefinition DeclaringTypeDefinition { get; }

    /// <summary>
    /// Gets parameters for callable members (methods) or index parameters for indexer properties.
    /// Returns null for fields and parameterless properties.
    /// </summary>
    IEnumerable<IParameter>? Parameters { get; }

    /// <summary>
    /// Gets whether this is a static member.
    /// </summary>
    bool IsStatic { get; }
}