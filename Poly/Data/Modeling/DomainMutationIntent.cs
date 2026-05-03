using System.Text.Json.Serialization;

using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "Type")]
[JsonDerivedType(typeof(SetDomainNameIntent), "SetDomainName")]
[JsonDerivedType(typeof(AddPrimitiveTypeIntent), "AddPrimitiveType")]
[JsonDerivedType(typeof(AddEntityTypeIntent), "AddEntityType")]
[JsonDerivedType(typeof(AddRelationshipIntent), "AddRelationship")]
public abstract record DomainMutationIntent;

public sealed record DomainNodeReference(string Path) {
    public static DomainNodeReference From(DomainType type) {
        ArgumentNullException.ThrowIfNull(type);
        return new DomainNodeReference(type.Name);
    }
}

public sealed record SetDomainNameIntent(string Name) : DomainMutationIntent;

public sealed record AddPrimitiveTypeIntent(string Name, TypeCategory Category) : DomainMutationIntent;

public sealed record AddEntityTypeIntent(string Name, DomainNodeReference? ParentEntity = null) : DomainMutationIntent;

public sealed record AddRelationshipIntent(
    string Name,
    DomainNodeReference SourceEntity,
    DomainNodeReference TargetEntity,
    RelationshipCardinality Cardinality,
    bool SourceOwnsTarget) : DomainMutationIntent;