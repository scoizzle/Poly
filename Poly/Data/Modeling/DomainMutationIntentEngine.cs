using Poly.Data.Modeling.Analysis;
using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling;

public sealed class DomainMutationIntentEngine {
    internal static TNode ResolveNode<TNode>(Domain domain, DomainNodeReference reference) where TNode : DomainMember {
        ArgumentNullException.ThrowIfNull(reference);

        var segments = reference.Path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (segments.Length == 0) {
            throw new ArgumentException("Node path cannot be null or empty.", nameof(reference));
        }

        var current = domain as DomainMember;

        foreach (var segment in segments) {
            var next = current.ChildObjects.OfType<DomainMember>().FirstOrDefault(child => child != null && string.Equals(child.Name, segment, StringComparison.Ordinal));

            if (next is null)
                throw new InvalidOperationException($"Could not resolve path '{reference.Path}' - segment '{segment}' not found under '{current.Name}'.");

            current = next;
        }

        if (current is not TNode typedNode) {
            throw new InvalidOperationException($"Could not resolve path '{reference.Path}' as {typeof(TNode).Name}.");
        }

        return typedNode;
    }

    public AnalysisResult Apply(Domain domain, DomainMutationIntent intent, AnalysisResult? preMutationAnalysis = null) {
        ArgumentNullException.ThrowIfNull(intent);
        return Apply(domain, [intent], preMutationAnalysis);
    }

    public AnalysisResult Apply(Domain domain, IEnumerable<DomainMutationIntent> intents, AnalysisResult? preMutationAnalysis = null) {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(intents);

        var mutation = domain.CreateMutation();

        foreach (var intent in intents) {
            ArgumentNullException.ThrowIfNull(intent);

            switch (intent) {
                case SetDomainNameIntent setDomainName:
                    _ = mutation.SetDomainName(setDomainName.Name);
                    break;

                case AddPrimitiveTypeIntent addPrimitiveType:
                    _ = mutation.AddType(new Primitive(domain, addPrimitiveType.Name, addPrimitiveType.Category));
                    break;

                case AddEntityTypeIntent addEntityType:
                    _ = mutation.AddType(CreateEntity(domain, addEntityType.Name, addEntityType.ParentEntity, isActor: false));
                    break;

                case AddActorTypeIntent addActorType:
                    _ = mutation.AddType(CreateEntity(domain, addActorType.Name, addActorType.ParentEntity, isActor: true));
                    break;

                case SetActorSubjectPropertyIntent setSubject: {
                        var actor = domain.RequireActor(setSubject.ActorName);
                        var property = string.IsNullOrWhiteSpace(setSubject.PropertyName)
                            ? null
                            : actor.RequireProperty(setSubject.PropertyName);
                        _ = mutation.SetActorSubjectProperty(actor, property);
                        break;
                    }

                case SetActorRoleClaimTypeIntent setRole: {
                        var actor = domain.RequireActor(setRole.ActorName);
                        _ = mutation.SetActorRoleClaimType(actor, string.IsNullOrWhiteSpace(setRole.RoleClaimType) ? null : setRole.RoleClaimType);
                        break;
                    }

                case AddActorClaimMappingIntent addMapping: {
                        var actor = domain.RequireActor(addMapping.ActorName);
                        var property = actor.RequireProperty(addMapping.PropertyName);
                        _ = mutation.AddActorClaimMapping(actor, new ActorClaimMapping(addMapping.ClaimType, property));
                        break;
                    }

                case RemoveActorClaimMappingIntent removeMapping: {
                        var actor = domain.RequireActor(removeMapping.ActorName);
                        var mapping = actor.ClaimMappings.FirstOrDefault(m => string.Equals(m.ClaimType, removeMapping.ClaimType, StringComparison.Ordinal))
                            ?? throw new InvalidOperationException($"Claim mapping for '{removeMapping.ClaimType}' not found on actor '{removeMapping.ActorName}'.");
                        _ = mutation.RemoveActorClaimMapping(actor, mapping);
                        break;
                    }

                case AddEventTypeIntent addEventType:
                    _ = mutation.AddType(new Event(domain, addEventType.Name));
                    break;

                case RemoveTypeIntent removeType: {
                        var type = domain.Types.FirstOrDefault(t => string.Equals(t.Name, removeType.Name, StringComparison.Ordinal))
                            ?? throw new InvalidOperationException($"Type '{removeType.Name}' was not found in domain '{domain.Name}'.");
                        _ = mutation.RemoveType(type);
                        break;
                    }

                case AddRelationshipIntent addRelationship: {
                        var rel = CreateRelationship(domain, addRelationship);
                        _ = mutation.AddRelationship(rel).AddEntityRelationship(rel.Source, rel);
                        break;
                    }

                case RemoveRelationshipIntent removeRelationship: {
                        var rel = domain.RequireRelationship(removeRelationship.Name);
                        _ = mutation.RemoveEntityRelationship(rel.Source, rel).RemoveRelationship(rel);
                        break;
                    }

                case SetRelationshipShapeIntent setShape: {
                        var rel = domain.RequireRelationship(setShape.RelationshipName);
                        var source = ResolveNode<Entity>(domain, setShape.Source);
                        var target = ResolveNode<Entity>(domain, setShape.Target);
                        _ = mutation.SetRelationship(rel, source, target, setShape.Cardinality, setShape.SourceOwnsTarget);
                        break;
                    }

                case AddPropertyToEntityIntent addProp: {
                        var entity = domain.RequireEntity(addProp.EntityName);
                        var type = RequireType(domain, addProp.TypeName);
                        _ = mutation.AddProperty(entity, new Property(domain, addProp.PropertyName, type));
                        break;
                    }

                case RemovePropertyFromEntityIntent removeProp: {
                        var entity = domain.RequireEntity(removeProp.EntityName);
                        var property = entity.RequireProperty(removeProp.PropertyName);
                        _ = mutation.RemoveProperty(entity, property);
                        break;
                    }

                case AddStageToEntityIntent addStage: {
                        var entity = domain.RequireEntity(addStage.EntityName);
                        var parent = string.IsNullOrWhiteSpace(addStage.ParentStageName) ? null : entity.RequireStage(addStage.ParentStageName);
                        var stage = new Stage(domain, addStage.StageName) { Parent = parent };
                        _ = mutation.AddStage(entity, stage);
                        break;
                    }

                case RemoveStageFromEntityIntent removeStage: {
                        var entity = domain.RequireEntity(removeStage.EntityName);
                        var stage = entity.RequireStage(removeStage.StageName);
                        _ = mutation.RemoveStage(entity, stage);
                        break;
                    }

                case AddActionToEntityIntent addAction: {
                        var entity = domain.RequireEntity(addAction.EntityName);
                        var action = new Action(domain, addAction.ActionName, entity);
                        _ = mutation.AddAction(entity, action);
                        break;
                    }

                case RemoveActionFromEntityIntent removeAction: {
                        var entity = domain.RequireEntity(removeAction.EntityName);
                        var action = entity.RequireAction(removeAction.ActionName);
                        _ = mutation.RemoveAction(entity, action);
                        break;
                    }

                case AddEventToEntityIntent addEvent: {
                        var entity = domain.RequireEntity(addEvent.EntityName);
                        var eventType = domain.RequireEventType(addEvent.EventTypeName);
                        _ = mutation.AddEvent(entity, eventType);
                        break;
                    }

                case RemoveEventFromEntityIntent removeEvent: {
                        var entity = domain.RequireEntity(removeEvent.EntityName);
                        var @event = entity.RequireEvent(removeEvent.EventTypeName);
                        _ = mutation.RemoveEvent(entity, @event);
                        break;
                    }

                case AddPropertyToEventTypeIntent addEventProp: {
                        var eventType = domain.RequireEventType(addEventProp.EventTypeName);
                        var type = RequireType(domain, addEventProp.TypeName);
                        _ = mutation.AddProperty(eventType, new Property(domain, addEventProp.PropertyName, type));
                        break;
                    }

                case RemovePropertyFromEventTypeIntent removeEventProp: {
                        var eventType = domain.RequireEventType(removeEventProp.EventTypeName);
                        var property = eventType.RequireProperty(removeEventProp.PropertyName);
                        _ = mutation.RemoveProperty(eventType, property);
                        break;
                    }

                case AddActionToStageIntent addStageAction: {
                        var entity = domain.RequireEntity(addStageAction.EntityName);
                        var stage = entity.RequireStage(addStageAction.StageName);
                        var action = entity.RequireAction(addStageAction.ActionName);
                        _ = mutation.AddAction(stage, action);
                        break;
                    }

                case RemoveActionFromStageIntent removeStageAction: {
                        var entity = domain.RequireEntity(removeStageAction.EntityName);
                        var stage = entity.RequireStage(removeStageAction.StageName);
                        var action = stage.RequireAction(removeStageAction.ActionName);
                        _ = mutation.RemoveAction(stage, action);
                        break;
                    }

                case AddActionParameterIntent addParam: {
                        var entity = domain.RequireEntity(addParam.EntityName);
                        var action = entity.RequireAction(addParam.ActionName);
                        var type = RequireType(domain, addParam.TypeName);
                        _ = mutation.AddParameter(action, new Property(domain, addParam.ParameterName, type));
                        break;
                    }

                case RemoveActionParameterIntent removeParam: {
                        var entity = domain.RequireEntity(removeParam.EntityName);
                        var action = entity.RequireAction(removeParam.ActionName);
                        var parameter = action.RequireParameter(removeParam.ParameterName);
                        _ = mutation.RemoveParameter(action, parameter);
                        break;
                    }

                case AddPolicyToEntityIntent addEntityPolicy: {
                        var entity = domain.RequireEntity(addEntityPolicy.EntityName);
                        var policy = new Policy(domain, addEntityPolicy.PolicyName) { AggregationStrategy = addEntityPolicy.Strategy };
                        _ = mutation.AddPolicy(entity, policy);
                        break;
                    }

                case RemovePolicyFromEntityIntent removeEntityPolicy: {
                        var entity = domain.RequireEntity(removeEntityPolicy.EntityName);
                        var policy = entity.RequirePolicy(removeEntityPolicy.PolicyName);
                        _ = mutation.RemovePolicy(entity, policy);
                        break;
                    }

                case AddPolicyToStageIntent addStagePolicy: {
                        var entity = domain.RequireEntity(addStagePolicy.EntityName);
                        var stage = entity.RequireStage(addStagePolicy.StageName);
                        var policy = new Policy(domain, addStagePolicy.PolicyName) { AggregationStrategy = addStagePolicy.Strategy };
                        _ = mutation.AddPolicy(stage, policy);
                        break;
                    }

                case RemovePolicyFromStageIntent removeStagePolicy: {
                        var entity = domain.RequireEntity(removeStagePolicy.EntityName);
                        var stage = entity.RequireStage(removeStagePolicy.StageName);
                        var policy = stage.RequirePolicy(removeStagePolicy.PolicyName);
                        _ = mutation.RemovePolicy(stage, policy);
                        break;
                    }

                case AddPolicyToPropertyIntent addPropPolicy: {
                        var entity = domain.RequireEntity(addPropPolicy.EntityName);
                        var property = entity.RequireProperty(addPropPolicy.PropertyName);
                        var policy = new Policy(domain, addPropPolicy.PolicyName) { AggregationStrategy = addPropPolicy.Strategy };
                        _ = mutation.AddPolicy(property, policy);
                        break;
                    }

                case RemovePolicyFromPropertyIntent removePropPolicy: {
                        var entity = domain.RequireEntity(removePropPolicy.EntityName);
                        var property = entity.RequireProperty(removePropPolicy.PropertyName);
                        var policy = property.RequirePolicy(removePropPolicy.PolicyName);
                        _ = mutation.RemovePolicy(property, policy);
                        break;
                    }

                case AddPolicyToActionIntent addActionPolicy: {
                        var entity = domain.RequireEntity(addActionPolicy.EntityName);
                        var action = entity.RequireAction(addActionPolicy.ActionName);
                        var policy = new Policy(domain, addActionPolicy.PolicyName) { AggregationStrategy = addActionPolicy.Strategy };
                        _ = mutation.AddPolicy(action, policy);
                        break;
                    }

                case RemovePolicyFromActionIntent removeActionPolicy: {
                        var entity = domain.RequireEntity(removeActionPolicy.EntityName);
                        var action = entity.RequireAction(removeActionPolicy.ActionName);
                        var policy = action.RequirePolicy(removeActionPolicy.PolicyName);
                        _ = mutation.RemovePolicy(action, policy);
                        break;
                    }

                case AddCrossPropertyRuleToPolicyIntent addCrossRule: {
                        var crossPolicy = ResolvePolicy(domain, addCrossRule.Target, addCrossRule.PolicyName);
                        var entity = domain.RequireEntity(addCrossRule.Target.EntityName);
                        var left = entity.RequireProperty(addCrossRule.LeftPropertyName);
                        var right = entity.RequireProperty(addCrossRule.RightPropertyName);
                        _ = mutation.AddRule(crossPolicy, new CrossPropertyRule(domain, addCrossRule.RuleName, left, right, addCrossRule.Operator));
                        break;
                    }

                case AddActorTypeRuleToPolicyIntent addActorTypeRule: {
                        var actorTypePolicy = ResolvePolicy(domain, addActorTypeRule.Target, addActorTypeRule.PolicyName);
                        var actorType = domain.RequireActor(addActorTypeRule.ActorTypeName);
                        _ = mutation.AddRule(actorTypePolicy, new ActorTypeRule(domain, addActorTypeRule.RuleName, actorType));
                        break;
                    }

                case AddActorRoleRuleToPolicyIntent addActorRoleRule: {
                        var actorRolePolicy = ResolvePolicy(domain, addActorRoleRule.Target, addActorRoleRule.PolicyName);
                        _ = mutation.AddRule(actorRolePolicy, new ActorRoleRule(domain, addActorRoleRule.RuleName, addActorRoleRule.Role));
                        break;
                    }

                case AddActorPropertyRuleToPolicyIntent addActorPropRule: {
                        var actorPropPolicy = ResolvePolicy(domain, addActorPropRule.Target, addActorPropRule.PolicyName);
                        var actor = domain.RequireActor(addActorPropRule.ActorTypeName);
                        var actorProperty = actor.RequireProperty(addActorPropRule.ActorPropertyName);
                        var constraint = new Poly.Data.Modeling.Validation.Constraints.EqualityConstraint(addActorPropRule.ConstraintValue);
                        _ = mutation.AddRule(actorPropPolicy, new ActorPropertyRule(domain, addActorPropRule.RuleName, actorProperty, constraint));
                        break;
                    }

                case AddCompositeRuleToPolicyIntent addComposite: {
                        var compositePolicy = ResolvePolicy(domain, addComposite.Target, addComposite.PolicyName);
                        var left = compositePolicy.RequireRule(addComposite.LeftRuleName);
                        var right = compositePolicy.RequireRule(addComposite.RightRuleName);
                        _ = mutation.AddRule(compositePolicy, new CompositeRule(domain, addComposite.RuleName, left, right, addComposite.Operator));
                        break;
                    }

                case RemoveRuleFromPolicyIntent removeRule: {
                        var removeRulePolicy = ResolvePolicy(domain, removeRule.Target, removeRule.PolicyName);
                        var rule = removeRulePolicy.RequireRule(removeRule.RuleName);
                        _ = mutation.RemoveRule(removeRulePolicy, rule);
                        break;
                    }

                case AddCommentIntent addComment: {
                        var node = ResolveNode<DomainMember>(domain, new DomainNodeReference(addComment.NodePath));
                        _ = mutation.AddComment(node, addComment.Comment);
                        break;
                    }

                default:
                    throw new NotSupportedException($"Unsupported domain mutation intent '{intent.GetType().Name}'.");
            }
        }

        return mutation.Apply(preMutationAnalysis);
    }

    private static DomainType RequireType(Domain domain, string typeName) {
        return domain.Types.FirstOrDefault(t => string.Equals(t.Name, typeName, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Type '{typeName}' was not found in domain '{domain.Name}'.");
    }

    private static Entity CreateEntity(Domain domain, string name, DomainNodeReference? parentEntity, bool isActor) {
        var parent = parentEntity is null
            ? null
            : ResolveNode<Entity>(domain, parentEntity);

        return isActor
            ? new Actor(domain, name, parent)
            : new Entity(domain, name, parent);
    }

    private static Relationship CreateRelationship(Domain domain, AddRelationshipIntent intent) {
        var source = ResolveNode<Entity>(domain, intent.SourceEntity);
        var target = ResolveNode<Entity>(domain, intent.TargetEntity);

        return new Relationship(domain, intent.Name, source, target, intent.Cardinality, intent.SourceOwnsTarget);
    }

    private static Policy ResolvePolicy(Domain domain, PolicyTarget target, string policyName) =>
        target switch {
            EntityPolicyTarget e => domain.RequireEntity(e.EntityName).RequirePolicy(policyName),
            StagePolicyTarget s => domain.RequireEntity(s.EntityName).RequireStage(s.StageName).RequirePolicy(policyName),
            ActionPolicyTarget a => domain.RequireEntity(a.EntityName).RequireAction(a.ActionName).RequirePolicy(policyName),
            PropertyPolicyTarget p => domain.RequireEntity(p.EntityName).RequireProperty(p.PropertyName).RequirePolicy(policyName),
            _ => throw new NotSupportedException($"Unsupported policy target type '{target.GetType().Name}'.")
        };
}