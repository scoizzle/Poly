using Poly.Validation;

namespace Poly.DataModeling;

public sealed record DateTimeProperty(string Name, IEnumerable<Constraint> Constraints, object? DefaultValue = null) : DataProperty(Name, Constraints, DefaultValue) {
    public override string ToString() => $"DateTime {Name}";
}