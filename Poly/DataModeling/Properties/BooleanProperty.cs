using Poly.Validation;

namespace Poly.DataModeling;

public sealed record BooleanProperty(string Name, IEnumerable<Constraint> Constraints) : DataProperty(Name, Constraints) {
    public override string ToString() => $"bool {Name}";
}