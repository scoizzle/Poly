namespace Poly.Introspection;

/// <summary>
/// Represents the access level of a member, indicating its visibility and accessibility from different parts of the codebase.
/// </summary>
public enum AccessModifier {
    /// <summary>
    /// Indicates that the member is accessible from any code that can reference the containing type. This is the most permissive access level.
    /// </summary>
    Public,
    /// <summary>
    /// Indicates that the member is accessible only within the containing type. This is the most restrictive access level.
    /// </summary>
    Private,

    /// <summary>
    /// Indicates that the member is accessible only within the containing assembly.
    /// </summary>
    Internal,
    /// <summary>
    /// Indicates that the member is accessible within the containing type and its derived types.
    /// </summary>
    Protected
}