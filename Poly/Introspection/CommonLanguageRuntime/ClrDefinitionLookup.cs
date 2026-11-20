namespace Poly.Introspection.CommonLanguageRuntime;

/// <summary>
/// The CLR's by-runtime-<see cref="Type"/> lookup, reached through a type system and its target chain (#998 T12b). It
/// lives here, where the CLR keeps its own facts, and is internal: a scoped consumer that has a runtime type asks the
/// chain, and a chain with no CLR source beneath it answers null (a ledger target states no CLR type).
/// </summary>
internal static class ClrDefinitionLookup {
    /// <summary>
    /// The definition for <paramref name="type"/> in <paramref name="system"/>'s own sources, then its target chain's.
    /// The first CLR registry that holds an answer decides it.
    /// </summary>
    public static ITypeDefinition? ClrDefinitionOf(this ITypeSystem system, Type type) {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(type);
        for (var current = system; current is not null; current = current.Target) {
            foreach (var source in current.Sources) {
                if (source is ClrTypeDefinitionRegistry registry
                    && registry.GetTypeDefinition(type) is { } definition)
                    return definition;
            }
        }
        return null;
    }
}