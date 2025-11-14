using Poly.Validation;

namespace Poly.DataModeling;

public sealed record DoubleProperty(string Name, IEnumerable<Constraint> Constraints) : DataProperty(Name, Constraints) {
    public override string ToString() => $"double {Name}";
}