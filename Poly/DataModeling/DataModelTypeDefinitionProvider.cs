using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.DataModeling;

public sealed class DataModelTypeDefinitionProvider : ITypeDefinitionProvider {
    private readonly Dictionary<string, ITypeDefinition> _typeDefinitions = new();
    private readonly ITypeDefinitionProvider _clrFallback = ClrTypeDefinitionRegistry.Shared;

    public void AddTypeDefinition(ITypeDefinition typeDefinition)
    {
        ArgumentNullException.ThrowIfNull(typeDefinition);
        _typeDefinitions.Add(typeDefinition.FullName, typeDefinition);
    }

    public void AddDataType(DataType dataType)
    {
        ArgumentNullException.ThrowIfNull(dataType);
        var typeDefinition = new DataTypeDefinition(dataType, this);
        _typeDefinitions.Add(typeDefinition.FullName, typeDefinition);
    }

    public ITypeDefinition? GetTypeDefinition(string name)
    {
        if (_typeDefinitions.TryGetValue(name, out var typeDef))
            return typeDef;
        
        return _clrFallback.GetTypeDefinition(name);
    }

    public ITypeDefinition? GetTypeDefinition(Type type) => _clrFallback.GetTypeDefinition(type);
}