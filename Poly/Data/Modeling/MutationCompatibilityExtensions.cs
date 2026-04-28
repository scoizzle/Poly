using Poly.Data.Modeling.Effects;
using Poly.Data.Modeling.TypeSystem;
using Poly.Data.Modeling.Validation;

namespace Poly.Data.Modeling;

/// <summary>
/// Transitional compatibility surface while callers migrate to nested mutator APIs.
/// </summary>
public static class MutationCompatibilityExtensions {
    public static void AddType(this Domain domain, DomainType type) {
        var mutation = domain.CreateMutation();
        _ = mutation.AddType(type);
        _ = ApplyOrThrowInvalidOperation(mutation);
    }

    public static void AddRelationship(this Domain domain, Relationship relationship) {
        var mutation = domain.CreateMutation();
        _ = mutation.AddRelationship(relationship);
        _ = ApplyOrThrowInvalidOperation(mutation);
    }

    public static void AddProperty(this Entity entity, Property property) {
        var mutation = entity.Domain.CreateMutation();
        _ = mutation.AddProperty(entity, property);
        _ = ApplyOrThrowInvalidOperation(mutation);
    }

    public static void AddStage(this Entity entity, Stage stage) {
        var mutation = entity.Domain.CreateMutation();
        _ = mutation.AddStage(entity, stage);
        _ = ApplyOrThrowInvalidOperation(mutation);
    }

    public static void AddPolicy(this Entity entity, Policy policy) {
        var mutation = entity.Domain.CreateMutation();
        _ = mutation.AddPolicy(entity, policy);
        _ = ApplyOrThrowInvalidOperation(mutation);
    }

    public static bool RemovePolicy(this Entity entity, Policy policy) {
        var mutation = entity.Domain.CreateMutation();
        _ = mutation.ExecuteValidatedMutation(Entity.CreateRemovePolicyMutation(entity, policy));
        _ = ApplyOrThrowInvalidOperation(mutation);
        return true;
    }

    public static void AddAction(this Entity entity, Action action) {
        var mutation = entity.Domain.CreateMutation();
        _ = mutation.AddAction(entity, action);
        _ = ApplyOrThrowInvalidOperation(mutation);
    }

    public static void AddEvent(this Entity entity, Event @event) {
        var mutation = entity.Domain.CreateMutation();
        _ = mutation.AddEvent(entity, @event);
        _ = ApplyOrThrowInvalidOperation(mutation);
    }

    public static void AddRelationship(this Entity entity, Relationship relationship) {
        var mutation = entity.Domain.CreateMutation();
        _ = mutation.AddEntityRelationship(entity, relationship);
        _ = ApplyOrThrowInvalidOperation(mutation);
    }

    public static void AddPolicy(this Stage stage, Policy policy) {
        var mutation = stage.Domain.CreateMutation();
        _ = mutation.AddPolicy(stage, policy);
        _ = ApplyOrThrowInvalidOperation(mutation);
    }

    public static bool RemovePolicy(this Stage stage, Policy policy) {
        var mutation = stage.Domain.CreateMutation();
        _ = mutation.ExecuteValidatedMutation(Stage.CreateRemovePolicyMutation(stage, policy));
        _ = ApplyOrThrowInvalidOperation(mutation);
        return true;
    }

    public static void AddAction(this Stage stage, Action action) {
        var mutation = stage.Domain.CreateMutation();
        _ = mutation.AddAction(stage, action);
        _ = ApplyOrThrowInvalidOperation(mutation);
    }

    public static bool RemoveAction(this Stage stage, Action action) {
        var mutation = stage.Domain.CreateMutation();
        _ = mutation.ExecuteValidatedMutation(Stage.CreateRemoveActionMutation(stage, action));
        _ = ApplyOrThrowInvalidOperation(mutation);
        return true;
    }

    public static void AddParameter(this Action action, IDomainValue parameter) {
        if (parameter is not Property property) {
            throw new InvalidOperationException("Action parameters must be properties.");
        }

        var mutation = action.Domain.CreateMutation();
        _ = mutation.AddParameter(action, property);
        _ = ApplyOrThrowInvalidOperation(mutation);
    }

    public static void AddEffect(this Action action, Effect effect) {
        var mutation = action.Domain.CreateMutation();
        _ = mutation.AddEffect(action, effect);
        _ = ApplyOrThrowInvalidOperation(mutation);
    }

    public static bool RemoveEffect(this Action action, Effect effect) {
        var mutation = action.Domain.CreateMutation();
        _ = mutation.ExecuteValidatedMutation(Action.CreateRemoveEffectMutation(action, effect));
        _ = ApplyOrThrowInvalidOperation(mutation);
        return true;
    }

    public static void AddProperty(this Event @event, Property property) {
        var mutation = @event.Domain.CreateMutation();
        _ = mutation.AddProperty(@event, property);
        _ = ApplyOrThrowInvalidOperation(mutation);
    }

    public static void AddConstraint(this Property property, Constraint constraint) {
        var mutation = property.Domain.CreateMutation();
        _ = mutation.AddConstraint(property, constraint);
        _ = ApplyOrThrowInvalidOperation(mutation);
    }

    public static void AddPolicy(this Property property, Policy policy) {
        var mutation = property.Domain.CreateMutation();
        _ = mutation.AddPolicy(property, policy);
        _ = ApplyOrThrowInvalidOperation(mutation);
    }

    public static bool RemovePolicy(this Property property, Policy policy) {
        var mutation = property.Domain.CreateMutation();
        _ = mutation.ExecuteValidatedMutation(Property.CreateRemovePolicyMutation(property, policy));
        _ = ApplyOrThrowInvalidOperation(mutation);
        return true;
    }

    public static void AddRule(this Policy policy, IPolicyRule rule) {
        var mutation = policy.Domain.CreateMutation();
        _ = mutation.AddRule(policy, rule);
        _ = ApplyOrThrowInvalidOperation(mutation);
    }

    public static bool RemoveRule(this Policy policy, IPolicyRule rule) {
        var mutation = policy.Domain.CreateMutation();
        _ = mutation.ExecuteValidatedMutation(Policy.CreateRemoveRuleMutation(policy, rule));
        _ = ApplyOrThrowInvalidOperation(mutation);
        return true;
    }

    public static void AddPolicy(this Relationship relationship, Policy policy) {
        var mutation = relationship.Domain.CreateMutation();
        _ = mutation.AddPolicy(relationship, policy);
        _ = ApplyOrThrowInvalidOperation(mutation);
    }

    public static void AddStage(this Relationship relationship, Stage stage) {
        var mutation = relationship.Domain.CreateMutation();
        _ = mutation.AddStage(relationship, stage);
        _ = ApplyOrThrowInvalidOperation(mutation);
    }

    public static void AddProperty(this Relationship relationship, Property property) {
        var mutation = relationship.Domain.CreateMutation();
        _ = mutation.AddProperty(relationship, property);
        _ = ApplyOrThrowInvalidOperation(mutation);
    }

    private static AnalysisResult ApplyOrThrowInvalidOperation(Domain.Mutation mutation) {
        try {
            var result = mutation.Apply();
            if (result.HasErrors) {
                var message = result.Diagnostics.FirstOrDefault(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)?.Message
                    ?? "Mutation apply reported one or more analysis errors.";
                throw new InvalidOperationException(message);
            }

            return result;
        }
        catch (DomainMutationValidationException ex) {
            var message = ex.Diagnostics.FirstOrDefault(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)?.Message
                ?? ex.Message;
            throw new InvalidOperationException(message, ex);
        }
    }
}