namespace Poly.DomainModeling.Contradictions;

/// <summary>Whether a bound flow blocks compilation. Only <see cref="Open"/> blocks.</summary>
public enum ContradictionStatus { Open, Resolved, Withdrawn }

/// <summary>The comparable family of an endpoint's type. Dialect type names are never compared.</summary>
public enum ContradictionFamily { Text, Number, Boolean, Temporal, Binary, Uuid }

/// <summary>
/// A fact the reader observed for one endpoint. A non-null field overrides the
/// domain's declared fact; MaxLength/IsNullable come from core constraints when
/// omitted. DisplayType is review text only and is never compared.
/// </summary>
public sealed record ContradictionShape(
    int? MaxLength = null,
    int? Precision = null,
    int? Scale = null,
    bool? IsNullable = null,
    ContradictionFamily? Family = null,
    string? DisplayType = null);

/// <summary>
/// One bound data flow between two entity properties (<c>Entity.Property</c> dotted outline
/// paths). <see cref="ClaimIds"/> carry the platform's provenance; the engine invents none.
/// </summary>
public sealed record ContradictionClaim(
    string Name,
    string SourceElementPath,
    string TargetElementPath,
    IReadOnlyList<string> ClaimIds,
    ContradictionStatus Status = ContradictionStatus.Open,
    ContradictionShape? SourceShape = null,
    ContradictionShape? TargetShape = null);