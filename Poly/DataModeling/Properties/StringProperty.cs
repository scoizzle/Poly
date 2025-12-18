using Poly.Validation;

namespace Poly.DataModeling;

public sealed record StringProperty(string Name, IEnumerable<Constraint> Constraints, object? DefaultValue = null) : DataProperty(Name, Constraints, DefaultValue) {
    public override string ToString() => $"string {Name}";
}