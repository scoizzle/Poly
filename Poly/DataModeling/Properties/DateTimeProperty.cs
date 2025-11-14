using Poly.Validation;

namespace Poly.DataModeling;

public sealed record DateTimeProperty(string Name, IEnumerable<Constraint> Constraints) : DataProperty(Name, Constraints) {
    public override string ToString() => $"datetime {Name}";
}