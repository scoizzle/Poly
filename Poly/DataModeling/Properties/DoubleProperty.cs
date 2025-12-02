using Poly.Validation;

namespace Poly.DataModeling;

public sealed record DoubleProperty(string Name, IEnumerable<Constraint> Constraints, object? DefaultValue = null) : DataProperty(Name, Constraints, DefaultValue) {
    public override string ToString() => $"double {Name}";
}