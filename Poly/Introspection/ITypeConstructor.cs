namespace Poly.Introspection;

/// <summary>
/// Represents a constructor on a type in the introspection system.
/// Constructors are callable members whose constructed type is the declaring type itself.
/// </summary>
public interface ITypeConstructor : ITypeMember {
    /// <summary>
    /// Gets the parameters for this constructor. Constructors always have parameters, even if empty.
    /// </summary>
    new IEnumerable<IParameter> Parameters { get; }
}