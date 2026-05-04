namespace Poly.Data.Modeling;

/// <summary>
/// Snapshot of how a runtime principal (e.g. JWT bearer token) maps to an <see cref="Actor"/> instance.
/// Pure data — evaluation is done at runtime by the host.
/// </summary>
public sealed record ActorIdentityProfile(
    Property? SubjectProperty,
    string? RoleClaimType,
    IReadOnlyCollection<ActorClaimMapping> ClaimMappings);

/// <summary>
/// Maps a single principal claim type to an actor property.
/// </summary>
public sealed record ActorClaimMapping(string ClaimType, Property Property);