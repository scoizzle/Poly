using Poly.Validation;

namespace Poly.DataModeling;

public sealed record StringProperty(string Name, IEnumerable<Constraint> Constraints) : DataProperty(Name, Constraints) {
    public override string ToString() => $"string {Name}";
}
