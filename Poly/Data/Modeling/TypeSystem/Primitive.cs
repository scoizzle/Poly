namespace Poly.Data.Modeling.TypeSystem;

public sealed record Primitive(Domain Domain, string Name, TypeCategory Category) : DomainType(Domain, Name) {
}