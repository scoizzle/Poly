using Poly.Validation;

namespace Poly.DataModeling;

public sealed record DecimalProperty(
    string Name,
    IEnumerable<Constraint> Constraints,
    object? DefaultValue = null,
    int? Precision = null,
    int? Scale = null
) : DataProperty(Name, Constraints, DefaultValue) {
    public override string ToString() => Precision.HasValue && Scale.HasValue 
        ? $"decimal({Precision},{Scale}) {Name}"
        : $"decimal {Name}";
}