using Poly.Validation;

namespace Poly.DataModeling;

public sealed record JsonProperty(
    string Name,
    string? SchemaDefinition = null,
    params IEnumerable<Constraint> Constraints
) : DataProperty(Name, Constraints) {
    public override string ToString() => $"json {Name}";
}