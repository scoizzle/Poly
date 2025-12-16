using Poly.Extensions;

namespace Poly.Introspection;

public interface ITypeDefinitionProvider {
    public ITypeDefinition? GetTypeDefinition(string name);

    public ITypeDefinition? GetTypeDefinition(Type type);

    public Lazy<ITypeDefinition> GetDeferredTypeDefinitionResolver(string name) {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new Lazy<ITypeDefinition>(
            () => GetTypeDefinition(name)
                ?? throw new ArgumentException($"Type with name '{name}' not found.", nameof(name)),
            isThreadSafe: true);
    }
}