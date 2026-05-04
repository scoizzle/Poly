using System.Text.Json.Serialization;

using Poly.Data.Modeling.TypeSystem;
using Poly.Data.Modeling.Validation;

namespace Poly.Data.Modeling;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "Type")]
[JsonDerivedType(typeof(SetDomainNameIntent), "SetDomainName")]
[JsonDerivedType(typeof(AddPrimitiveTypeIntent), "AddPrimitiveType")]
[JsonDerivedType(typeof(AddEntityTypeIntent), "AddEntityType")]
[JsonDerivedType(typeof(AddActorTypeIntent), "AddActorType")]
[JsonDerivedType(typeof(SetActorSubjectPropertyIntent), "SetActorSubjectProperty")]
[JsonDerivedType(typeof(SetActorRoleClaimTypeIntent), "SetActorRoleClaimType")]
[JsonDerivedType(typeof(AddActorClaimMappingIntent), "AddActorClaimMapping")]
[JsonDerivedType(typeof(RemoveActorClaimMappingIntent), "RemoveActorClaimMapping")]
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
[JsonDerivedType(typeof(AddPolicyToEntityIntent), "AddPolicyToEntity")]
[JsonDerivedType(typeof(RemovePolicyFromEntityIntent), "RemovePolicyFromEntity")]
[JsonDerivedType(typeof(AddPolicyToStageIntent), "AddPolicyToStage")]
[JsonDerivedType(typeof(RemovePolicyFromStageIntent), "RemovePolicyFromStage")]
[JsonDerivedType(typeof(AddPolicyToPropertyIntent), "AddPolicyToProperty")]
[JsonDerivedType(typeof(RemovePolicyFromPropertyIntent), "RemovePolicyFromProperty")]
[JsonDerivedType(typeof(AddCrossPropertyRuleToPolicyIntent), "AddCrossPropertyRuleToPolicy")]
[JsonDerivedType(typeof(AddActorTypeRuleToPolicyIntent), "AddActorTypeRuleToPolicy")]
[JsonDerivedType(typeof(AddActorRoleRuleToPolicyIntent), "AddActorRoleRuleToPolicy")]
[JsonDerivedType(typeof(AddCompositeRuleToPolicyIntent), "AddCompositeRuleToPolicy")]
[JsonDerivedType(typeof(RemoveRuleFromPolicyIntent), "RemoveRuleFromPolicy")]
[JsonDerivedType(typeof(AddPolicyToActionIntent), "AddPolicyToAction")]
[JsonDerivedType(typeof(RemovePolicyFromActionIntent), "RemovePolicyFromAction")]
[JsonDerivedType(typeof(AddCommentIntent), "AddComment")]
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

public sealed record AddActorTypeIntent(string Name, DomainNodeReference? ParentEntity = null) : DomainMutationIntent;

public sealed record SetActorSubjectPropertyIntent(string ActorName, string? PropertyName) : DomainMutationIntent;

public sealed record SetActorRoleClaimTypeIntent(string ActorName, string? RoleClaimType) : DomainMutationIntent;

public sealed record AddActorClaimMappingIntent(string ActorName, string ClaimType, string PropertyName) : DomainMutationIntent;

public sealed record RemoveActorClaimMappingIntent(string ActorName, string ClaimType) : DomainMutationIntent;

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

// ── Policy attachment intents ──────────────────────────────────────────────

public sealed record AddPolicyToEntityIntent(
    string EntityName,
    string PolicyName,
    PolicyAggregationStrategy Strategy = PolicyAggregationStrategy.All) : DomainMutationIntent;

public sealed record RemovePolicyFromEntityIntent(
    string EntityName,
    string PolicyName) : DomainMutationIntent;

public sealed record AddPolicyToStageIntent(
    string EntityName,
    string StageName,
    string PolicyName,
    PolicyAggregationStrategy Strategy = PolicyAggregationStrategy.All) : DomainMutationIntent;

public sealed record RemovePolicyFromStageIntent(
    string EntityName,
    string StageName,
    string PolicyName) : DomainMutationIntent;

public sealed record AddPolicyToPropertyIntent(
    string EntityName,
    string PropertyName,
    string PolicyName,
    PolicyAggregationStrategy Strategy = PolicyAggregationStrategy.All) : DomainMutationIntent;

public sealed record RemovePolicyFromPropertyIntent(
    string EntityName,
    string PropertyName,
    string PolicyName) : DomainMutationIntent;

public sealed record AddPolicyToActionIntent(
    string EntityName,
    string ActionName,
    string PolicyName,
    PolicyAggregationStrategy Strategy = PolicyAggregationStrategy.All) : DomainMutationIntent;

public sealed record RemovePolicyFromActionIntent(
    string EntityName,
    string ActionName,
    string PolicyName) : DomainMutationIntent;

// ── Rule intents ─────────────────────────────────────────────────────────

/// <summary>Adds a cross-property comparison rule to a policy on an entity.</summary>
public sealed record AddCrossPropertyRuleToPolicyIntent(
    string EntityName,
    string PolicyName,
    string RuleName,
    string LeftPropertyName,
    string RightPropertyName,
    DomainComparisonOperator Operator) : DomainMutationIntent;

/// <summary>Adds a rule requiring the evaluating actor to be of the given actor type.</summary>
public sealed record AddActorTypeRuleToPolicyIntent(
    string EntityName,
    string PolicyName,
    string RuleName,
    string ActorTypeName) : DomainMutationIntent;

/// <summary>Adds a rule requiring the evaluating actor to have a specific role claim value.</summary>
public sealed record AddActorRoleRuleToPolicyIntent(
    string EntityName,
    string PolicyName,
    string RuleName,
    string Role) : DomainMutationIntent;

/// <summary>Combines two existing rules in the same policy with And / Or.</summary>
public sealed record AddCompositeRuleToPolicyIntent(
    string EntityName,
    string PolicyName,
    string RuleName,
    string LeftRuleName,
    string RightRuleName,
    LogicalOperator Operator) : DomainMutationIntent;

/// <summary>Removes a rule from a policy on an entity.</summary>
public sealed record RemoveRuleFromPolicyIntent(
    string EntityName,
    string PolicyName,
    string RuleName) : DomainMutationIntent;

[JsonDerivedType(typeof(AddCommentIntent), "AddComment")]
public sealed record AddCommentIntent(string NodePath, string Comment) : DomainMutationIntent;