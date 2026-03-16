namespace Poly.DomainModeling;

public sealed record RelationshipEnd(
    string TypeName,
    string? PropertyName,
    IEnumerable<Validation.Constraint>? Constraints
);

public abstract record Relationship(
    string Name,
    RelationshipEnd Source,
    RelationshipEnd Target,
    IEnumerable<DataProperty>? Properties = null,
    IEnumerable<Validation.Constraint>? RelationshipConstraints = null
);

public sealed record OneToOneRelationship(
    string Name,
    RelationshipEnd Source,
    RelationshipEnd Target,
    IEnumerable<DataProperty>? Properties = null,
    IEnumerable<Validation.Constraint>? RelationshipConstraints = null
) : Relationship(Name, Source, Target, Properties, RelationshipConstraints);

public sealed record OneToManyRelationship(
    string Name,
    RelationshipEnd Source,
    RelationshipEnd Target,
    IEnumerable<DataProperty>? Properties = null,
    IEnumerable<Validation.Constraint>? RelationshipConstraints = null
) : Relationship(Name, Source, Target, Properties, RelationshipConstraints);

public sealed record ManyToOneRelationship(
    string Name,
    RelationshipEnd Source,
    RelationshipEnd Target,
    IEnumerable<DataProperty>? Properties = null,
    IEnumerable<Validation.Constraint>? RelationshipConstraints = null
) : Relationship(Name, Source, Target, Properties, RelationshipConstraints);

public sealed record ManyToManyRelationship(
    string Name,
    RelationshipEnd Source,
    RelationshipEnd Target,
    IEnumerable<DataProperty>? Properties = null,
    IEnumerable<Validation.Constraint>? RelationshipConstraints = null
) : Relationship(Name, Source, Target, Properties, RelationshipConstraints);

public sealed record InheritanceRelationship(
    string Name,
    RelationshipEnd Source,
    RelationshipEnd Target,
    IEnumerable<DataProperty>? Properties = null,
    IEnumerable<Validation.Constraint>? RelationshipConstraints = null
) : Relationship(Name, Source, Target, Properties, RelationshipConstraints);

public sealed record AssociationRelationship(
    string Name,
    RelationshipEnd Source,
    RelationshipEnd Target,
    IEnumerable<DataProperty>? Properties = null,
    IEnumerable<Validation.Constraint>? RelationshipConstraints = null
) : Relationship(Name, Source, Target, Properties, RelationshipConstraints);