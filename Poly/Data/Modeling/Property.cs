using Poly.Data.Modeling.TypeSystem;
using Poly.Data.Modeling.Validation;

namespace Poly.Data.Modeling;

public sealed class Property : IDomainValue {
    private readonly List<Constraint> _constraints = [];

    public string Name { get; set; } = string.Empty;
    public IDomainType Type { get; set; } = null!;
    public IReadOnlyCollection<Constraint> Constraints => _constraints;

    public void AddConstraint(Constraint constraint) => _constraints.Add(constraint);
}