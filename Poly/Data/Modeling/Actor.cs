namespace Poly.Data.Modeling;

/// <summary>
/// Represents an actor in the system, which can be a user, service, or any entity that can perform actions and have claims.
/// </summary>
public sealed partial record Actor : Entity {
    private readonly List<ActorClaimMapping> _claimMappings = [];

    public Actor(Domain domain, string name, Entity? parentEntity = null) : base(domain, name, parentEntity) {
    }

    public Property? SubjectProperty { get; private set; }
    public string? RoleClaimType { get; private set; }
    public IReadOnlyCollection<ActorClaimMapping> ClaimMappings => _claimMappings;

    public ActorIdentityProfile IdentityProfile => new(SubjectProperty, RoleClaimType, ClaimMappings);
}