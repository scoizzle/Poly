using Poly.Introspection;

namespace Poly.DataModeling;

public sealed class DataModelTypeDefinitionProvider : ITypeDefinitionProvider {
    private readonly Dictionary<string, ITypeDefinition> _typeDefinitions = new();

    public void AddTypeDefinition(ITypeDefinition typeDefinition)
    {
        ArgumentNullException.ThrowIfNull(typeDefinition);
        _typeDefinitions.Add(typeDefinition.FullName, typeDefinition);
    }

    public ITypeDefinition? GetTypeDefinition(string name) => _typeDefinitions.TryGetValue(name, out var typeDef) ? typeDef : null;

    public ITypeDefinition? GetTypeDefinition(Type type) => default;
}