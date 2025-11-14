using Poly.Validation;

namespace Poly.DataModeling;

public sealed record DecimalProperty(
    string Name,
    int? Precision = null,
    int? Scale = null,
    params IEnumerable<Constraint> Constraints
) : DataProperty(Name, Constraints) {
    public override string ToString() => Precision.HasValue && Scale.HasValue 
        ? $"decimal({Precision},{Scale}) {Name}"
        : $"decimal {Name}";
}