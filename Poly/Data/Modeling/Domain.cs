using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling;

public sealed partial record Domain : DomainMember {
    private readonly Lock _mutationLock = new();
    private readonly List<DomainObject> _objects = new();

    public Domain(string name) : base(name) { }

    // Type-specific helpers are now provided as extension methods in DomainExtensions.cs
    public IReadOnlyCollection<DomainObject> Objects => _objects.AsReadOnly();
    public sealed override IEnumerable<DomainObject> ChildObjects => [.. _objects];
}