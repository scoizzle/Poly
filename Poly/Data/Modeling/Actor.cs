namespace Poly.Data.Modeling;

public sealed partial record Actor : Entity {
    private readonly List<ActorClaimMapping> _claimMappings = [];

    public Actor(Domain domain, string name, Entity? parentEntity = null) : base(domain, name, parentEntity) {
    }

    public Property? SubjectProperty { get; private set; }
    public string? RoleClaimType { get; private set; }
    public IReadOnlyCollection<ActorClaimMapping> ClaimMappings => _claimMappings;

    public ActorIdentityProfile IdentityProfile => new(SubjectProperty, RoleClaimType, ClaimMappings);
}