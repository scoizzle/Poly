namespace Poly.Introspection;

/// <summary>
/// Represents a property member of a type in the introspection system.
/// Properties may have getter/setter accessors and optional index parameters.
/// </summary>
public interface ITypeProperty : ITypeMember {
    // Inherits all from ITypeMember; no additional members needed for properties
}