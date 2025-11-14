using Poly.Validation;

namespace Poly.DataModeling;

public sealed record DateOnlyProperty(string Name, IEnumerable<Constraint> Constraints) : DataProperty(Name, Constraints) {
    public override string ToString() => $"date {Name}";
}