using Poly.DomainModeling.Ontology;
using Poly.Introspection;

namespace Poly.DomainModeling.Meaning;

internal sealed class DomainTypeDefinitionProvider(Domain domain, TypeMappingRegistry typeMaps) : ITypeDefinitionProvider {
    private readonly Domain _domain = domain;
    private readonly TypeMappingRegistry _typeMaps = typeMaps;

    public ITypeDefinition? GetTypeDefinition(string name) => throw new NotImplementedException();

    public ITypeDefinition? GetTypeDefinition(Type type) => throw new NotImplementedException();
}