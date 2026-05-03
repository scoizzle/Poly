using Poly.Data.Modeling.TypeSystem;
using Poly.Data.Modeling.Validation;

namespace Poly.Data.Modeling;

public sealed partial record Property(Domain Domain, string Name, DomainType Type) : DomainValue(Domain, Name, Type) {
    private readonly List<Constraint> _constraints = [];
    private readonly List<Policy> _policies = [];

    public IReadOnlyCollection<Constraint> Constraints => _constraints.AsReadOnly();
    public IReadOnlyCollection<Policy> Policies => _policies.AsReadOnly();

    public sealed override IEnumerable<DomainMember> ChildObjects => [/* TODO: .. _constraints, */.. _policies];
}