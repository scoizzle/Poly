using System.Reflection;

namespace Poly.Introspection.CommonLanguageRuntime;

/// <summary>
/// The types one CLR assembly declares, by namespace. The definitions are the CLR registry's own instances, so a type
/// reached through a source is the same type reached any other way.
/// </summary>
public sealed class ClrAssemblySource : ITypeSource {
    private readonly Lazy<ILookup<string?, Type>> _byNamespace;

    public ClrAssemblySource(Assembly assembly) {
        ArgumentNullException.ThrowIfNull(assembly);
        _byNamespace = new(() => Declared(assembly).ToLookup(static type => type.Namespace, StringComparer.Ordinal));
    }

    public IEnumerable<ITypeDefinition> TypesIn(string? @namespace) =>
        _byNamespace.Value[@namespace].Select(ClrTypeDefinitionRegistry.Shared.GetTypeDefinition);

    /// <summary>The types the assembly declares at namespace level, minus the compiler's own (closures, anonymous
    /// types), which no source text can name. A type that fails to load is skipped: the rest are still what the
    /// assembly contains.</summary>
    private static IEnumerable<Type> Declared(Assembly assembly) {
        Type?[] types;
        try {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException partial) {
            types = partial.Types;
        }
        return types.Where(static type => type is { IsNested: false } && !type.Name.Contains('<', StringComparison.Ordinal))!;
    }
}