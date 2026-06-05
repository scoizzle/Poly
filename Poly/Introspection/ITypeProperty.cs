namespace Poly.Introspection;

/// <summary>
/// Represents a property member of a type in the introspection system.
/// Properties may have getter/setter accessors and optional index parameters.
/// </summary>
public interface ITypeProperty : ITypeMember {
    /// <summary>
    /// Optional delegate to read the property's value (or indexer get).
    /// </summary>
    MemberReadDelegate? Read { get; }

    /// <summary>
    /// Optional delegate to write the property's value (or indexer set).
    /// </summary>
    MemberWriteDelegate? Write { get; }

    /// <summary>
    /// Optional delegate to initialize the property (init-only setter).
    /// </summary>
    MemberWriteDelegate? Initialize { get; }
}