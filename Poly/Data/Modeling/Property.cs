using Poly.Data.Modeling.TypeSystem;
using Poly.Data.Modeling.Validation;

namespace Poly.Data.Modeling;

public sealed record Property : DomainObject, IDomainValue {
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
    public string Name { get; set; }
    public IReadOnlyCollection<Constraint> Constraints => _constraints.AsReadOnly();
    public IReadOnlyCollection<Policy> Policies => _policies.AsReadOnly();

    public void AddConstraint(Constraint constraint) {
        ArgumentNullException.ThrowIfNull(constraint);

        if (_constraints.Contains(constraint)) {
            throw new InvalidOperationException($"Constraint '{constraint.GetType().Name}' already exists on property '{Name}'.");
        }

        _constraints.Add(constraint);
    }

    public bool RemoveConstraint(Constraint constraint) {
        ArgumentNullException.ThrowIfNull(constraint);
        return _constraints.Remove(constraint);
    }

    public void AddPolicy(Policy policy) {
        policy.ThrowIfNullOrMismatchedDomain(Domain);

        if (_policies.Any(existing => string.Equals(existing.Name, policy.Name, StringComparison.Ordinal))) {
            throw new InvalidOperationException($"Policy '{policy.Name}' already exists on property '{Name}'.");
        }

        _policies.Add(policy);
    }

    public bool RemovePolicy(Policy policy) {
        policy.ThrowIfNullOrMismatchedDomain(Domain);
        return _policies.Remove(policy);
    }
}