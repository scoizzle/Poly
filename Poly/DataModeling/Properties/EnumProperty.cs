using Poly.Validation;

namespace Poly.DataModeling;

public sealed record EnumProperty(
    string Name,
    string EnumTypeName,
    IEnumerable<string> AllowedValues,
    IEnumerable<Constraint> Constraints,
    object? DefaultValue = null
) : DataProperty(Name, Constraints, DefaultValue) {
    public override string ToString() => $"{EnumTypeName} {Name}";
}