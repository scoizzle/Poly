namespace Poly.DataModeling;

public sealed record RelationshipEnd(
    string TypeName,
    string? PropertyName,
    IEnumerable<Poly.Validation.Constraint>? Constraints
);

public abstract record Relationship(
    string Name,
    RelationshipEnd Source,
    RelationshipEnd Target
);

public sealed record OneToOneRelationship(
    string Name,
    RelationshipEnd Source,
    RelationshipEnd Target
) : Relationship(Name, Source, Target);

public sealed record OneToManyRelationship(
    string Name,
    RelationshipEnd Source,
    RelationshipEnd Target
) : Relationship(Name, Source, Target);

public sealed record ManyToOneRelationship(
    string Name,
    RelationshipEnd Source,
    RelationshipEnd Target
) : Relationship(Name, Source, Target);

public sealed record ManyToManyRelationship(
    string Name,
    RelationshipEnd Source,
    RelationshipEnd Target
) : Relationship(Name, Source, Target);

public sealed record InheritanceRelationship(
    string Name,
    RelationshipEnd Source,
    RelationshipEnd Target
) : Relationship(Name, Source, Target);

public sealed record AssociationRelationship(
    string Name,
    RelationshipEnd Source,
    RelationshipEnd Target
) : Relationship(Name, Source, Target);