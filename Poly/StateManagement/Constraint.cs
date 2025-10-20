namespace Poly.StateManagement;

public abstract record Constraint(string propertyName) : Rule
{
    public string PropertyName { get; init; } = propertyName;
}