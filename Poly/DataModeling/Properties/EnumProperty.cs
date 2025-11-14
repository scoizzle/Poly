using Poly.Validation;

namespace Poly.DataModeling;

public sealed record EnumProperty(
    string Name,
    string EnumTypeName,
    IEnumerable<string> AllowedValues,
    params IEnumerable<Constraint> Constraints
) : DataProperty(Name, Constraints) {
    public override string ToString() => $"{EnumTypeName} {Name}";
}