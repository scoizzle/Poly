namespace Poly.Introspection;

public interface ITypeDefinitionProvider {
    public ITypeDefinition? GetType(string name);

    public Lazy<ITypeDefinition> GetDeferredTypeDefinitionResolver(string name) {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new Lazy<ITypeDefinition>(() => GetType(name) ?? throw new KeyNotFoundException($"Type with name '{name}' not found."), isThreadSafe: true);
    }
}