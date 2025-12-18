using Poly.Interpretation;

namespace Poly.Introspection;

/// <summary>
/// Represents a field member of a type in the introspection system.
/// Fields are storage locations with a type and no parameters.
/// </summary>
public interface ITypeField : ITypeMember {
    // Inherits all from ITypeMember; no additional members needed for fields
}