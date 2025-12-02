using Poly.Validation;

namespace Poly.DataModeling;

/// <summary>
/// Represents a property that references another type defined in the DataModel.
/// </summary>
public sealed record ReferenceProperty(
    string Name,
    string ReferencedTypeName,
    IEnumerable<Constraint> Constraints,
    object? DefaultValue = null
) : DataProperty(Name, Constraints, DefaultValue) {
    public override string ToString() => $"{ReferencedTypeName} {Name}";
}
