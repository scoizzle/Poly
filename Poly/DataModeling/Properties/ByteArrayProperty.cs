using Poly.Validation;

namespace Poly.DataModeling;

public sealed record ByteArrayProperty(
    string Name,
    IEnumerable<Constraint> Constraints,
    object? DefaultValue = null,
    int? MaxLength = null
) : DataProperty(Name, Constraints, DefaultValue) {
    public override string ToString() => MaxLength.HasValue 
        ? $"byte[{MaxLength}] {Name}"
        : $"byte[] {Name}";
}