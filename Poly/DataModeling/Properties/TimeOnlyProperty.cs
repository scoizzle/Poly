using Poly.Validation;

namespace Poly.DataModeling;

public sealed record TimeOnlyProperty(string Name, IEnumerable<Constraint> Constraints) : DataProperty(Name, Constraints) {
    public override string ToString() => $"time {Name}";
}