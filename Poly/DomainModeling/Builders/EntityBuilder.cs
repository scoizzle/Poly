using Poly.DomainModeling.Builders;

namespace Poly.DomainModeling;

public sealed class EntityBuilder : DomainMemberBuilder {
    private readonly string _name;
    private readonly List<Property> _properties = new();
    private readonly List<Action> _actions = new();
    private readonly List<Policy> _policies = new();
    private readonly List<Stage> _stages = new();

    public string Name { get; private set; }

    internal EntityBuilder(DomainBuilder domainBuilder, string name) : base(domainBuilder) {
        _name = Guard.ThrowIfNullOrEmpty(name);
        Name = _name;
    }

    // Basic property and event support (simplified)
    public EntityBuilder Property(string name, string ofType) {
        _properties.Add(new Property(
            Guard.ThrowIfNullOrEmpty(name),
            new DomainTypeReference(Guard.ThrowIfNullOrEmpty(ofType)),
            []
        ));
        return this;
    }

    /// <summary>
    /// Declares an owned value structure (composition).
    /// The resulting ValueType is automatically registered with the Domain.
    /// </summary>
    public ValueBuilder OwnsOne(string name) {
        // Delegate to DomainBuilder so the ValueType (and any properties configured on it)
        // are properly tracked and included in the final Domain.Types.
        return _domainBuilder.ValueType(Guard.ThrowIfNullOrEmpty(name));
    }

    public EntityBuilder OwnsOne(string name, Action<ValueBuilder> configure) {
        ArgumentNullException.ThrowIfNull(configure);
        var vb = OwnsOne(name);
        configure(vb);
        return this;
    }

    /// <summary>
    /// Declares ownership of a value type that was pre-declared on the domain (e.g. via .Type(...)).
    /// This is the ergonomic form shown in the target V3 examples.
    /// </summary>
    public EntityBuilder OwnsOne(string name, string ofType) {
        // For now we just ensure the owned name is registered as a ValueType alias.
        // Full resolution happens in analysis. We still go through ValueType so it is tracked.
        _domainBuilder.ValueType(Guard.ThrowIfNullOrEmpty(name));
        return this;
    }

    public StageBuilder Stage(string name) {
        return new StageBuilder(Guard.ThrowIfNullOrEmpty(name));
    }

    public EntityBuilder Stage(string name, Action<StageBuilder> configure) {
        ArgumentNullException.ThrowIfNull(configure);
        var stageBuilder = Stage(name);
        configure(stageBuilder);
        _stages.Add(stageBuilder.Build());
        return this;
    }

    public ActionBuilder Action(string name) {
        return new ActionBuilder(Guard.ThrowIfNullOrEmpty(name));
    }

    public EntityBuilder Action(string name, Action<ActionBuilder> configure) {
        ArgumentNullException.ThrowIfNull(configure);
        var actionBuilder = Action(name);
        configure(actionBuilder);
        _actions.Add(actionBuilder.Build());
        return this;
    }

    public EntityBuilder Policy(string name, DomainExpression expression) {
        _policies.Add(new Policy(Guard.ThrowIfNullOrEmpty(name), expression));
        return this;
    }

    [Obsolete("Prefer the DomainExpression overload.")]
    public EntityBuilder Policy(string name, Constraint constraint) {
        // Legacy path - create a simple policy
        _policies.Add(new Policy(Guard.ThrowIfNullOrEmpty(name), DomainExpression.Property("true")));
        return this;
    }

    public EntityBuilder Policy(Policy policy) {
        _policies.Add(policy);
        return this;
    }

    internal new Entity Build() {
        return new Entity(
            _name,
            _properties,
            _actions,
            _policies,
            _stages
        );
    }

    /// <summary>
    /// Declares a HasMany relationship from this entity.
    /// The relationship is registered at the Domain level.
    /// </summary>
    public EntityBuilder HasMany(string name, string targetType) {
        _domainBuilder.Relationship(
            Guard.ThrowIfNullOrEmpty(name),
            _name,
            Guard.ThrowIfNullOrEmpty(targetType),
            RelationshipCardinality.OneToMany
        );
        return this;
    }

    public EntityBuilder HasOne(string name, string targetType) {
        _domainBuilder.Relationship(
            Guard.ThrowIfNullOrEmpty(name),
            _name,
            Guard.ThrowIfNullOrEmpty(targetType),
            RelationshipCardinality.OneToOne
        );
        return this;
    }
}

// === Builder Surface Sketch (for future implementation) ===
//
// These are proposed fluent shapes that the V3 builders should expose
// once the supporting *Builder types are implemented.
//
// Example desired usage (aligned with PersonLifecycleExample):
//
// .Entity("Person")
//     .OwnsOne("BirthCertificate")
//         .Property("Time", "Timestamp")
//     .Stage("Alive")
//         .Policy("HasBirthCertificate", expr => expr
//             .Satisfies(owned => owned.Owned("BirthCertificate").Property("Time").Exists(), new RequiredConstraint()))
//         .Policy("NoDeathCertificate", expr => expr
//             .Satisfies(owned => owned.Owned("DeathCertificate").Property("Time").NotExists(), new RequiredConstraint()))
//         .OnEntry(publish => publish
//             .Event("Born")
//             .Bind("TimeOfBirth", expr => expr.Owned("BirthCertificate").Property("Time")))
//         .Action("Die")
//             .Parameter("TimeOfDeath", "Timestamp")
//             .Parameter("CauseOfDeath", "Text")
//             .Effect(create => create
//                 .Entity("DeathCertificate")
//                 .Set("Time", p => p.Parameter("TimeOfDeath"))
//                 .Set("Cause", p => p.Parameter("CauseOfDeath")))
//             .Effect(transition => transition.ToStage("Dead"))
//
// The goal is for the builder to feel declarative while the immutable model
// (Stage + Policy + Effect + DomainExpression) remains the source of truth.
// Stage invariants are expressed as Policies on the stage using the unified expression system.