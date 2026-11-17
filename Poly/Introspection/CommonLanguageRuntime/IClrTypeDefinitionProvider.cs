namespace Poly.Introspection.CommonLanguageRuntime;

/// <summary>
/// A provider that resolves definitions from CLR runtime types. A platform with no CLR types does not implement this:
/// the neutral <see cref="ITypeDefinitionProvider"/> asks only by name and by primitive, so nothing in it requires a
/// host to have <see cref="Type"/> handles it cannot supply.
/// </summary>
public interface IClrTypeDefinitionProvider : ITypeDefinitionProvider {
    /// <summary>
    /// Resolves a type definition by runtime <see cref="Type"/>.
    /// Returns null when not found.
    /// </summary>
    ITypeDefinition? GetTypeDefinition(Type type);
}