namespace Poly.DomainModeling.V2;

public enum DomainMutationKind {
    AddEntity,
    AddProperty,
    AddStage,
    AddAction,
    AddActionEffect,
    AddRelationship
}

public sealed record DomainMutation(
    DomainMutationKind Kind,
    string? Name = null,
    string? EntityName = null,
    string? TargetEntityName = null,
    string? Type = null,
    bool IsRequired = false,
    bool IsInitialStage = false,
    string? DefaultValue = null,
    RelationshipKind? RelationshipKind = null,
    string? ActionName = null,
    IEffect? Effect = null
) {
    public static DomainMutation AddEntity(string entityName) => new(DomainMutationKind.AddEntity, Name: entityName);

    public static DomainMutation AddProperty(string entityName, string propertyName, string type, bool isRequired = false, string? defaultValue = null)
        => new(DomainMutationKind.AddProperty, Name: propertyName, EntityName: entityName, Type: type, IsRequired: isRequired, DefaultValue: defaultValue);

    public static DomainMutation AddStage(string entityName, string stageName, bool isInitial = false)
        => new(DomainMutationKind.AddStage, Name: stageName, EntityName: entityName, IsInitialStage: isInitial);

    public static DomainMutation AddAction(string entityName, string actionName)
        => new(DomainMutationKind.AddAction, Name: actionName, EntityName: entityName);

    public static DomainMutation AddActionEffect(string entityName, string actionName, IEffect effect)
        => new(DomainMutationKind.AddActionEffect, EntityName: entityName, ActionName: actionName, Effect: effect);

    public static DomainMutation AddRelationship(string name, string sourceEntity, string targetEntity, RelationshipKind kind)
        => new(DomainMutationKind.AddRelationship, Name: name, EntityName: sourceEntity, TargetEntityName: targetEntity, RelationshipKind: kind);
}
