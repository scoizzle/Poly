namespace Poly.StateManagement;

public abstract record Constraint(string memberName) : Rule
{
    public string Member { get; init; } = memberName;
}