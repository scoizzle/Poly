using Poly.Validation;

namespace Poly.DataModeling;

/// <summary>
/// Represents a property that references another type defined in the DataModel.
/// </summary>
public sealed record ReferenceProperty(
    string Name,
    string ReferencedTypeName,
    params IEnumerable<Constraint> Constraints
) : DataProperty(Name, Constraints) {
    public override string ToString() => $"{ReferencedTypeName} {Name}";
}
