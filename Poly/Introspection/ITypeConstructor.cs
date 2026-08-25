namespace Poly.Introspection;

/// <summary>
/// Represents a constructor on a type in the introspection system.
/// Constructors are callable members whose constructed type is the declaring type itself.
/// </summary>
public interface ITypeConstructor : ITypeMember { }