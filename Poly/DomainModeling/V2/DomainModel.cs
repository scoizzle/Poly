namespace Poly.DomainModeling.V2;

public sealed record Domain(
    string Name,
    IReadOnlyList<Entity> Entities,
    IReadOnlyList<Relationship> Relationships
) {
    public IReadOnlyList<DomainType> Types => Entities;
}

public record DomainType(string Name, IReadOnlyList<Property> Properties);

public sealed record Entity(
    string Name,
    IReadOnlyList<Property> Properties,
    IReadOnlyList<Stage> Stages,
    IReadOnlyList<Action> Actions
) : DomainType(Name, Properties);

public sealed record Property(
    string Name,
    string Type,
    bool IsRequired = false,
    string? DefaultValue = null
);

public sealed record Stage(string Name, bool IsInitial = false);

public sealed record Action(
    string Name,
    IReadOnlyList<Property> Parameters,
    IReadOnlyList<IEffect> Effects
);

public enum RelationshipKind {
    OneToOne,
    OneToMany,
    ManyToOne,
    ManyToMany
}

public sealed record Relationship(
    string Name,
    string SourceEntity,
    string TargetEntity,
    RelationshipKind Kind
);
