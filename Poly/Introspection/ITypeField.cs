namespace Poly.Introspection;

/// <summary>
/// Delegate for reading a member value.
/// </summary>
public delegate object? MemberReadDelegate(object owner, object?[]? arguments);

/// <summary>
/// Delegate for writing (or initializing) a member value.
/// </summary>
public delegate object MemberWriteDelegate(object owner, object? value, object?[]? arguments);

/// <summary>
/// Represents a field member of a type in the introspection system.
/// Fields are storage locations with a type and no parameters.
/// </summary>
public interface ITypeField : ITypeMember {
    /// <summary>
    /// Optional delegate to read the field's value. Null if not readable (rare for fields).
    /// </summary>
    MemberReadDelegate? Read { get; }

    /// <summary>
    /// Optional delegate to write the field's value.
    /// </summary>
    MemberWriteDelegate? Write { get; }

    /// <summary>
    /// Optional delegate to initialize the field (for init-only or readonly fields).
    /// </summary>
    MemberWriteDelegate? Initialize { get; }
}