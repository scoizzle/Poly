using Poly.Data.Modeling.Effects;
using Poly.Data.Modeling.TypeSystem;
using Poly.Data.Modeling.Validation;

namespace Poly.Data.Modeling;

public static class MutationApply {
    public static void AddType(Domain domain, DomainType type) => Apply(domain, mutation => mutation.AddType(type));

    public static void AddRelationship(Domain domain, Relationship relationship) => Apply(domain, mutation => mutation.AddRelationship(relationship));

    public static void AddRelationship(Entity entity, Relationship relationship) => Apply(entity.Domain, mutation => mutation.AddEntityRelationship(entity, relationship));

    public static void AddProperty(Entity entity, Property property) => Apply(entity.Domain, mutation => mutation.AddProperty(entity, property));

    public static void AddProperty(Event @event, Property property) => Apply(@event.Domain, mutation => mutation.AddProperty(@event, property));

    public static void AddProperty(Relationship relationship, Property property) => Apply(relationship.Domain, mutation => mutation.AddProperty(relationship, property));

    public static void AddStage(Entity entity, Stage stage) => Apply(entity.Domain, mutation => mutation.AddStage(entity, stage));

    public static void AddStage(Relationship relationship, Stage stage) => Apply(relationship.Domain, mutation => mutation.AddStage(relationship, stage));

    public static void AddPolicy(Entity entity, Policy policy) => Apply(entity.Domain, mutation => mutation.AddPolicy(entity, policy));

    public static void AddPolicy(Stage stage, Policy policy) => Apply(stage.Domain, mutation => mutation.AddPolicy(stage, policy));

    public static void AddPolicy(Property property, Policy policy) => Apply(property.Domain, mutation => mutation.AddPolicy(property, policy));

    public static void AddPolicy(Relationship relationship, Policy policy) => Apply(relationship.Domain, mutation => mutation.AddPolicy(relationship, policy));

    public static bool RemovePolicy(Stage stage, Policy policy) {
        try {
            _ = stage.Domain.CreateMutation().RemovePolicy(stage, policy).Apply();
        }
        catch (DomainMutationValidationException ex) {
            var message = ex.Diagnostics.FirstOrDefault(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)?.Message
                ?? ex.Message;
            throw new InvalidOperationException(message, ex);
        }

        return true;
    }

    public static void AddAction(Entity entity, Action action) => Apply(entity.Domain, mutation => mutation.AddAction(entity, action));

    public static void AddAction(Stage stage, Action action) => Apply(stage.Domain, mutation => mutation.AddAction(stage, action));

    public static void AddEvent(Entity entity, Event @event) => Apply(entity.Domain, mutation => mutation.AddEvent(entity, @event));

    public static void AddParameter(Action action, Property parameter) => Apply(action.Domain, mutation => mutation.AddParameter(action, parameter));

    public static void AddEffect(Action action, Effect effect) => Apply(action.Domain, mutation => mutation.AddEffect(action, effect));

    public static void AddRule(Policy policy, IPolicyRule rule) => Apply(policy.Domain, mutation => mutation.AddRule(policy, rule));

    public static void AddConstraint(Property property, Constraint constraint) => Apply(property.Domain, mutation => mutation.AddConstraint(property, constraint));

    private static void Apply(Domain domain, Func<Domain.Mutation, Domain.Mutation> applyMutation) {
        try {
            _ = applyMutation(domain.CreateMutation()).Apply();
        }
        catch (DomainMutationValidationException ex) {
            var message = ex.Diagnostics.FirstOrDefault(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)?.Message
                ?? ex.Message;
            throw new InvalidOperationException(message, ex);
        }
    }
}