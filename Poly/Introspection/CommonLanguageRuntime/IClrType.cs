namespace Poly.Introspection.CommonLanguageRuntime;

/// <summary>
/// CLR-specific extension of <see cref="ITypeDefinition"/> representing a type defined in the Common Language Runtime.
/// </summary>
public interface IClrTypeDefinition : ITypeDefinition {
    public Type RuntimeType { get; }
}