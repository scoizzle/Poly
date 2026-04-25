using Poly.Data.Modeling.TypeSystem;
using Poly.Data.Modeling.Validation;

namespace Poly.Data.Modeling;

public sealed class Property : IDomainValue {
    private readonly List<Constraint> _constraints = [];

    public required Domain Domain { get; init; }
    public required string Name { get; set; }
    public required IDomainType Type { get; set; }
    public IReadOnlyCollection<Constraint> Constraints { get => _constraints; init => _constraints.AddRange(value); }

    public void AddConstraint(Constraint constraint) => _constraints.Add(constraint);
}