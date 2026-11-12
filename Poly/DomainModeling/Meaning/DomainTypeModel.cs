using Poly.DomainModeling.Ontology;
using Poly.Introspection;

namespace Poly.DomainModeling.Meaning;

internal sealed class DomainTypeModel(Domain domain, TypeMappingRegistry typeMaps) {
    private readonly Domain _domain = domain;
    private readonly TypeMappingRegistry _typeMaps = typeMaps;

    public ExprType Resolve(string domainName) => throw new NotImplementedException();

    public ExprType OfProperty(Property property) => throw new NotImplementedException();

    public Assignability Assign(ExprType from, ExprType to) => throw new NotImplementedException();

    public NumericKind KindOf(ExprType type) => throw new NotImplementedException();
}