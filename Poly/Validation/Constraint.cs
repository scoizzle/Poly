namespace Poly.Validation;

public abstract class Constraint(string propertyName) : Rule {
    public string PropertyName { get; init; } = propertyName;
}