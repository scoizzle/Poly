using Poly.Validation;

namespace Poly.DataModeling;

public sealed record GuidProperty(string Name, IEnumerable<Constraint> Constraints) : DataProperty(Name, Constraints) {
    public override string ToString() => $"guid {Name}";
}