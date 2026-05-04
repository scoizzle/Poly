using System.Text.Json.Serialization;

using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "Type")]
[JsonDerivedType(typeof(SetDomainNameIntent), "SetDomainName")]
[JsonDerivedType(typeof(AddPrimitiveTypeIntent), "AddPrimitiveType")]
[JsonDerivedType(typeof(AddEntityTypeIntent), "AddEntityType")]
[JsonDerivedType(typeof(AddEventTypeIntent), "AddEventType")]
[JsonDerivedType(typeof(RemoveTypeIntent), "RemoveType")]
[JsonDerivedType(typeof(AddRelationshipIntent), "AddRelationship")]
[JsonDerivedType(typeof(RemoveRelationshipIntent), "RemoveRelationship")]
[JsonDerivedType(typeof(SetRelationshipShapeIntent), "SetRelationshipShape")]
[JsonDerivedType(typeof(AddPropertyToEntityIntent), "AddPropertyToEntity")]
[JsonDerivedType(typeof(RemovePropertyFromEntityIntent), "RemovePropertyFromEntity")]
[JsonDerivedType(typeof(AddStageToEntityIntent), "AddStageToEntity")]
[JsonDerivedType(typeof(RemoveStageFromEntityIntent), "RemoveStageFromEntity")]
[JsonDerivedType(typeof(AddActionToEntityIntent), "AddActionToEntity")]
[JsonDerivedType(typeof(RemoveActionFromEntityIntent), "RemoveActionFromEntity")]
[JsonDerivedType(typeof(AddEventToEntityIntent), "AddEventToEntity")]
[JsonDerivedType(typeof(RemoveEventFromEntityIntent), "RemoveEventFromEntity")]
[JsonDerivedType(typeof(AddPropertyToEventTypeIntent), "AddPropertyToEventType")]
[JsonDerivedType(typeof(RemovePropertyFromEventTypeIntent), "RemovePropertyFromEventType")]
[JsonDerivedType(typeof(AddActionToStageIntent), "AddActionToStage")]
[JsonDerivedType(typeof(RemoveActionFromStageIntent), "RemoveActionFromStage")]
[JsonDerivedType(typeof(AddActionParameterIntent), "AddActionParameter")]
[JsonDerivedType(typeof(RemoveActionParameterIntent), "RemoveActionParameter")]
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

public sealed record AddEventTypeIntent(string Name) : DomainMutationIntent;

public sealed record RemoveTypeIntent(string Name) : DomainMutationIntent;

public sealed record AddRelationshipIntent(
    string Name,
    DomainNodeReference SourceEntity,
    DomainNodeReference TargetEntity,
    RelationshipCardinality Cardinality,
    bool SourceOwnsTarget) : DomainMutationIntent;

public sealed record RemoveRelationshipIntent(string Name) : DomainMutationIntent;

public sealed record SetRelationshipShapeIntent(
    string RelationshipName,
    DomainNodeReference Source,
    DomainNodeReference Target,
    RelationshipCardinality Cardinality,
    bool SourceOwnsTarget) : DomainMutationIntent;

public sealed record AddPropertyToEntityIntent(
    string EntityName,
    string PropertyName,
    string TypeName) : DomainMutationIntent;

public sealed record RemovePropertyFromEntityIntent(
    string EntityName,
    string PropertyName) : DomainMutationIntent;

public sealed record AddStageToEntityIntent(
    string EntityName,
    string StageName,
    string? ParentStageName = null) : DomainMutationIntent;

public sealed record RemoveStageFromEntityIntent(
    string EntityName,
    string StageName) : DomainMutationIntent;

public sealed record AddActionToEntityIntent(
    string EntityName,
    string ActionName) : DomainMutationIntent;

public sealed record RemoveActionFromEntityIntent(
    string EntityName,
    string ActionName) : DomainMutationIntent;

public sealed record AddEventToEntityIntent(
    string EntityName,
    string EventTypeName) : DomainMutationIntent;

public sealed record RemoveEventFromEntityIntent(
    string EntityName,
    string EventTypeName) : DomainMutationIntent;

public sealed record AddPropertyToEventTypeIntent(
    string EventTypeName,
    string PropertyName,
    string TypeName) : DomainMutationIntent;

public sealed record RemovePropertyFromEventTypeIntent(
    string EventTypeName,
    string PropertyName) : DomainMutationIntent;

public sealed record AddActionToStageIntent(
    string EntityName,
    string StageName,
    string ActionName) : DomainMutationIntent;

public sealed record RemoveActionFromStageIntent(
    string EntityName,
    string StageName,
    string ActionName) : DomainMutationIntent;

public sealed record AddActionParameterIntent(
    string EntityName,
    string ActionName,
    string ParameterName,
    string TypeName) : DomainMutationIntent;

public sealed record RemoveActionParameterIntent(
    string EntityName,
    string ActionName,
    string ParameterName) : DomainMutationIntent;