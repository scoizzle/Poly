using Poly.Validation.Rules;

namespace Poly.Validation.Builders;

public sealed class ConstraintSetBuilder<T>(string propertyName) {
    private readonly List<Constraint> _constraints = [];
    public string PropertyName => propertyName;
    public IEnumerable<Constraint> Constraints => _constraints;

    internal ConstraintSetBuilder<T> Add(Constraint constraint) {
        _constraints.Add(constraint);
        return this;
    }

    public Rule Build() => new AndRule(_constraints);
}