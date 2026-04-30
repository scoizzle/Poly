using Poly.Data.Modeling.TypeSystem;
using Poly.Data.Modeling.Validation;

namespace Poly.Data.Modeling;

public sealed partial record Property : DomainObject, IDomainValue {
    private readonly List<Constraint> _constraints = [];
    private readonly List<Policy> _policies = [];

    public Property(Domain domain, string name, IDomainType type) : base(domain) {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(type);

        type.ThrowIfMismatchedDomain(domain);

        Name = name;
        Type = type;
    }

    public IDomainType Type { get; }
    public IReadOnlyCollection<Constraint> Constraints => _constraints.AsReadOnly();
    public IReadOnlyCollection<Policy> Policies => _policies.AsReadOnly();

    public sealed override IEnumerable<DomainObject> ChildObjects => [/* TODO: .. _constraints, */.. _policies];
}