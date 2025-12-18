namespace Poly.Introspection;

/// <summary>
/// Represents a method member of a type in the introspection system.
/// Methods are callable with parameters and may have multiple overloads.
/// </summary>
public interface ITypeMethod : ITypeMember {
    /// <summary>
    /// Gets the parameters for this method. Methods always have parameters, even if empty.
    /// </summary>
    new IEnumerable<IParameter> Parameters { get; }
}