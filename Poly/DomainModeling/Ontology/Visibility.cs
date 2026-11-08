namespace Poly.DomainModeling.Ontology;

/// <summary>
/// Export visibility of a domain member. One enum for every member kind.
/// Higher values are more visible. The gap between <see cref="Internal"/> and
/// <see cref="Public"/> is reserved so a later level (module-scoped, friend)
/// can slot in without renumbering.
/// </summary>
public enum Visibility {
    /// <summary>
    /// Visible inside the declaring domain. Absent from the exported contract. The default.
    /// </summary>
    Internal = 0,

    /// <summary>
    /// Marked <c>public</c>. Belongs on the exported contract.
    /// </summary>
    Public = 100,
}