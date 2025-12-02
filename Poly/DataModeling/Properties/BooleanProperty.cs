using Poly.Validation;

namespace Poly.DataModeling;

public sealed record BooleanProperty(string Name, IEnumerable<Constraint> Constraints, object? DefaultValue = null) : DataProperty(Name, Constraints, DefaultValue) {
    public override string ToString() => $"bool {Name}";
}