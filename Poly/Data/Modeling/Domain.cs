using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling;

public sealed partial record Domain : DomainObject {
    private readonly Lock _mutationLock = new();
    private readonly List<DomainType> _types = new();
    private readonly List<Relationship> _relationships = new();

    public Domain(string name) => Name = name;

    public IReadOnlyCollection<DomainType> Types => _types.AsReadOnly();
    public IReadOnlyCollection<Relationship> Relationships => _relationships.AsReadOnly();
    public sealed override IEnumerable<DomainObject> ChildObjects => [.. _types, .. _relationships];
}