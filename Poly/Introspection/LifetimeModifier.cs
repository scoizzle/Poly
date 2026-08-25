namespace Poly.Introspection;

/// <summary>
/// Represents the lifetime of a member, indicating whether it is associated with the type itself (static) or with instances of the type (instance).
/// </summary>
public enum LifetimeModifier {
    /// <summary>
    /// Indicates that the member belongs to the type itself rather than any instance of the type.
    /// Static members can be accessed without creating an instance of the type and are shared across all instances. They are typically used for utility functions, constants, or data that should be common to all instances of the type.
    /// </summary>
    Static,
    /// <summary>
    /// Indicates that the member belongs to an instance of the type. Instance members require an object of the type to be created before they can be accessed. They can hold state specific to that instance and can be different across different instances of the same type.
    /// </summary>
    Instance
}