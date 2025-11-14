using Poly.Validation;

namespace Poly.DataModeling;

public sealed record Int64Property(string Name, IEnumerable<Constraint> Constraints) : DataProperty(Name, Constraints) {
    public override string ToString() => $"long {Name}";
}