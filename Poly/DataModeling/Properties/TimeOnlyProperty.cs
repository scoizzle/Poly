using Poly.Validation;

namespace Poly.DataModeling;

public sealed record TimeOnlyProperty(string Name, IEnumerable<Constraint> Constraints, object? DefaultValue = null) : DataProperty(Name, Constraints, DefaultValue) {
    public override string ToString() => $"TimeOnly {Name}";
}