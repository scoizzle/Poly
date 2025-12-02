using Poly.Validation;

namespace Poly.DataModeling;

public sealed record JsonProperty(
    string Name,
    IEnumerable<Constraint> Constraints,
    object? DefaultValue = null,
    string? SchemaDefinition = null
) : DataProperty(Name, Constraints, DefaultValue) {
    public override string ToString() => $"json {Name}";
}