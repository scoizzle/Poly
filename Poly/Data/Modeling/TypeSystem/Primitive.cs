using Poly.Data.Modeling.Validation;

namespace Poly.Data.Modeling.TypeSystem;

public sealed record Primitive : DomainType {
    public Primitive(Domain domain, string name, TypeCategory category, IEnumerable<Constraint>? constraints = null)
        : base(domain, name, constraints ?? []) {
        Category = category;
    }

    public TypeCategory Category { get; init; }
}