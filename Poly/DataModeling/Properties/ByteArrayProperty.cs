using Poly.Validation;

namespace Poly.DataModeling;

public sealed record ByteArrayProperty(
    string Name,
    int? MaxLength = null,
    params IEnumerable<Constraint> Constraints
) : DataProperty(Name, Constraints) {
    public override string ToString() => MaxLength.HasValue 
        ? $"byte[{MaxLength}] {Name}"
        : $"byte[] {Name}";
}