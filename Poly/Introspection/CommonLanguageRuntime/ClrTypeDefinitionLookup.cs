namespace Poly.Introspection.CommonLanguageRuntime;

/// <summary>
/// The CLR handle asked of a provider that may not have one. A provider with no CLR types answers null here, so a
/// consumer can ask for a runtime type without the neutral contract requiring every platform to honour it.
/// </summary>
internal static class ClrTypeDefinitionLookup {
    /// <summary>The definition for <paramref name="type"/>, or null when the provider has no CLR types or no such type.</summary>
    public static ITypeDefinition? GetClrTypeDefinition(this ITypeDefinitionProvider provider, Type type) {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(type);
        return provider is IClrTypeDefinitionProvider clr ? clr.GetTypeDefinition(type) : null;
    }
}