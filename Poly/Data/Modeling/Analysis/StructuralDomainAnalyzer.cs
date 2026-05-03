using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling;

internal sealed class StructuralDomainAnalyzer : INodeAnalyzer {
    public void Analyze(AnalysisContext context, Node node) {
        if (!context.ShouldAnalyze(node)) {
            return;
        }

        switch (node) {
            case Domain request:
                AnalyzeDomain(context, request.Domain);
                break;
            case Relationship relationship:
                ValidateStandaloneRelationship(context, relationship);
                AnalyzeEntityStandalone(context, relationship);
                break;
            case Entity entity:
                AnalyzeEntityStandalone(context, entity);
                break;
            case Stage stage:
                AnalyzeStageStandalone(context, stage);
                break;
            case Action action:
                AnalyzeActionStandalone(context, action);
                break;
            case Event @event:
                AnalyzeEventStandalone(context, @event);
                break;
            case Property property:
                AnalyzePropertyStandalone(context, property);
                break;
        }

        this.AnalyzeChildren(context, node);
    }

    private static void AnalyzeEntityStandalone(AnalysisContext context, Entity entity) {
        if (!context.TryBeginAnalyzerVisit<StructuralDomainAnalyzer>(entity)) {
            return;
        }

        ReportDuplicateNames(context, entity, entity.Properties, static property => property.Name, "property");
        ReportDuplicateNames(context, entity, entity.Stages, static stage => stage.Name, "stage");
        ReportDuplicateNames(context, entity, entity.Actions, static action => action.Name, "action");
        ReportDuplicateNames(context, entity, entity.Policies, static policy => policy.Name, "policy");
        ReportDuplicateNames(context, entity, entity.Events, static @event => @event.Name, "event");
        ReportDuplicateNames(context, entity, entity.Relationships, static relationship => relationship.Name, "relationship");

        foreach (var stage in entity.Stages) {
            ReportDuplicateNames(context, stage, stage.Policies, static policy => policy.Name, "policy");
            ReportDuplicateNames(context, stage, stage.Actions, static action => action.Name, "action");
        }

        foreach (var action in entity.Actions.Concat(entity.Stages.SelectMany(static stage => stage.Actions))) {
            ReportDuplicateNames(context, action, action.Parameters, static parameter => parameter.Name, "parameter");
        }

        foreach (var @event in entity.Events) {
            ReportDuplicateNames(context, @event, @event.Properties, static property => property.Name, "property");
        }

        foreach (var property in entity.Properties.Concat(entity.Events.SelectMany(static @event => @event.Properties))) {
            ReportDuplicateNames(context, property, property.Policies, static policy => policy.Name, "policy");
        }

        ValidateEntityMembership(context, entity.Domain, entity);
        ValidateParentCycle(context, entity);
    }

    private static void AnalyzeStageStandalone(AnalysisContext context, Stage stage) {
        if (!context.TryBeginAnalyzerVisit<StructuralDomainAnalyzer>(stage)) {
            return;
        }

        ReportDuplicateNames(context, stage, stage.Policies, static policy => policy.Name, "policy");
        ReportDuplicateNames(context, stage, stage.Actions, static action => action.Name, "action");

        foreach (var policy in stage.Policies) {
            if (!ReferenceEquals(policy.Domain, stage.Domain)) {
                context.ReportError(
                    stage,
                    $"Policy '{policy.Name}' must belong to domain '{stage.Domain.Name}'.",
                    DomainModelDiagnosticCodes.MutationInvariant);
            }
        }

        foreach (var action in stage.Actions) {
            if (!ReferenceEquals(action.Domain, stage.Domain)) {
                context.ReportError(
                    stage,
                    $"Action '{action.Name}' must belong to domain '{stage.Domain.Name}'.",
                    DomainModelDiagnosticCodes.MutationInvariant);
            }

            if (stage.OwnerEntity is not null && !ReferenceEquals(action.Entity, stage.OwnerEntity)) {
                context.ReportError(
                    action,
                    $"Action '{action.Name}' on stage '{stage.Name}' must belong to entity '{stage.OwnerEntity.Name}'.",
                    DomainModelDiagnosticCodes.MutationInvariant);
            }
        }
    }

    private static void AnalyzeActionStandalone(AnalysisContext context, Action action) {
        if (!context.TryBeginAnalyzerVisit<StructuralDomainAnalyzer>(action)) {
            return;
        }

        ReportDuplicateNames(context, action, action.Parameters, static parameter => parameter.Name, "parameter");

        foreach (var parameter in action.Parameters) {
            if (!ReferenceEquals(parameter.Domain, action.Domain)) {
                context.ReportError(
                    action,
                    $"Parameter '{parameter.Name}' must belong to domain '{action.Domain.Name}'.",
                    DomainModelDiagnosticCodes.MutationInvariant);
            }
        }
    }

    private static void AnalyzeEventStandalone(AnalysisContext context, Event @event) {
        if (!context.TryBeginAnalyzerVisit<StructuralDomainAnalyzer>(@event)) {
            return;
        }

        ReportDuplicateNames(context, @event, @event.Properties, static property => property.Name, "property");

        foreach (var property in @event.Properties) {
            if (!ReferenceEquals(property.Domain, @event.Domain)) {
                context.ReportError(
                    @event,
                    $"Property '{property.Name}' must belong to domain '{@event.Domain.Name}'.",
                    DomainModelDiagnosticCodes.MutationInvariant);
            }
        }
    }

    private static void AnalyzePropertyStandalone(AnalysisContext context, Property property) {
        if (!context.TryBeginAnalyzerVisit<StructuralDomainAnalyzer>(property)) {
            return;
        }

        ReportDuplicateNames(context, property, property.Policies, static policy => policy.Name, "policy");

        foreach (var policy in property.Policies) {
            if (!ReferenceEquals(policy.Domain, property.Domain)) {
                context.ReportError(
                    property,
                    $"Policy '{policy.Name}' must belong to domain '{property.Domain.Name}'.",
                    DomainModelDiagnosticCodes.MutationInvariant);
            }
        }
    }

    private static void ValidateStandaloneRelationship(AnalysisContext context, Relationship relationship) {
        if (!context.TryBeginAnalyzerVisit<StructuralDomainAnalyzer>(relationship)) {
            return;
        }

        if (relationship.Source is null || relationship.Target is null) {
            context.ReportError(
                relationship,
                $"Relationship '{relationship.Name}' must have both source and target defined.",
                DomainModelDiagnosticCodes.MutationInvariant);
            return;
        }

        if (!ReferenceEquals(relationship.Source.Domain, relationship.Domain)
            || !ReferenceEquals(relationship.Target.Domain, relationship.Domain)) {
            context.ReportError(
                relationship,
                $"Relationship '{relationship.Name}' source and target must belong to domain '{relationship.Domain.Name}'.",
                DomainModelDiagnosticCodes.MutationInvariant);
        }

        if (relationship.SourceOwnsTarget) {
            if (relationship.Source is not Entity || relationship.Target is not Entity) {
                context.ReportError(
                    relationship,
                    $"Ownership relationship '{relationship.Name}' requires entity source and entity target.",
                    DomainModelDiagnosticCodes.StructuralOwnership);
            }

            if (relationship.Cardinality is RelationshipCardinality.ManyToOne or RelationshipCardinality.ManyToMany) {
                context.ReportError(
                    relationship,
                    $"Ownership relationship '{relationship.Name}' must be one-to-one or one-to-many.",
                    DomainModelDiagnosticCodes.StructuralOwnership);
            }
        }
    }

    private static void AnalyzeDomain(AnalysisContext context, Domain domain) {
        if (!context.TryBeginAnalyzerVisit<StructuralDomainAnalyzer>(domain)) {
            return;
        }

        ReportDuplicateNames(context, domain, domain.Types.Where(context.ShouldAnalyze), static type => type.Name, "type");
        ReportDuplicateNames(context, domain, domain.Relationships.Where(context.ShouldAnalyze), static relationship => relationship.Name, "relationship");

        ValidateDomainMembership(context, domain);
        ValidateRelationshipEndpoints(context, domain);
        ValidateOwnershipCardinality(context, domain);
        ValidateOwnershipTargetUniqueness(context, domain);
    }

    private static void ValidateDomainMembership(AnalysisContext context, Domain domain) {
        foreach (var type in domain.Types.Where(context.ShouldAnalyze)) {
            if (!ReferenceEquals(type.Domain, domain)) {
                context.ReportError(
                    type,
                    $"Type '{type.Name}' does not belong to domain '{domain.Name}'.",
                    DomainModelDiagnosticCodes.MutationInvariant);
            }
        }

        foreach (var relationship in domain.Relationships.Where(context.ShouldAnalyze)) {
            if (!ReferenceEquals(relationship.Domain, domain)) {
                context.ReportError(
                    relationship,
                    $"Relationship '{relationship.Name}' does not belong to domain '{domain.Name}'.",
                    DomainModelDiagnosticCodes.MutationInvariant);
            }
        }
    }

    private static void ValidateEntityMembership(AnalysisContext context, Domain domain, Entity entity) {
        static void ReportMismatchedDomain(AnalysisContext context, Node owner, DomainMember child, Domain domain, string childLabel, string childName) {
            if (!ReferenceEquals(child.Domain, domain)) {
                context.ReportError(
                    owner,
                    $"{childLabel} '{childName}' must belong to domain '{domain.Name}'.",
                    DomainModelDiagnosticCodes.MutationInvariant);
            }
        }

        foreach (var property in entity.Properties) {
            ReportMismatchedDomain(context, entity, property, domain, "Property", property.Name);
        }

        foreach (var stage in entity.Stages) {
            ReportMismatchedDomain(context, entity, stage, domain, "Stage", stage.Name);
        }

        foreach (var policy in entity.Policies) {
            ReportMismatchedDomain(context, entity, policy, domain, "Policy", policy.Name);
        }

        foreach (var action in entity.Actions) {
            ReportMismatchedDomain(context, entity, action, domain, "Action", action.Name);

            if (!ReferenceEquals(action.Entity, entity)) {
                context.ReportError(
                    action,
                    $"Action '{action.Name}' must belong to entity '{entity.Name}'.",
                    DomainModelDiagnosticCodes.MutationInvariant);
            }

            foreach (var parameter in action.Parameters) {
                ReportMismatchedDomain(context, action, parameter, domain, "Parameter", parameter.Name);
            }
        }

        foreach (var @event in entity.Events) {
            ReportMismatchedDomain(context, entity, @event, domain, "Event", @event.Name);

            foreach (var property in @event.Properties) {
                ReportMismatchedDomain(context, @event, property, domain, "Property", property.Name);
            }
        }

        foreach (var relationship in entity.Relationships) {
            ReportMismatchedDomain(context, entity, relationship, domain, "Relationship", relationship.Name);

            if (!domain.Relationships.Contains(relationship)) {
                context.ReportError(
                    relationship,
                    $"Relationship '{relationship.Name}' must be registered in domain '{domain.Name}' before attaching to entity '{entity.Name}'.",
                    DomainModelDiagnosticCodes.MutationInvariant);
            }

            if (!ReferenceEquals(relationship.Source, entity)) {
                context.ReportError(
                    relationship,
                    $"Relationship '{relationship.Name}' source must match entity '{entity.Name}'.",
                    DomainModelDiagnosticCodes.MutationInvariant);
            }
        }

        foreach (var stage in entity.Stages) {
            foreach (var policy in stage.Policies) {
                ReportMismatchedDomain(context, stage, policy, domain, "Policy", policy.Name);
            }

            foreach (var action in stage.Actions) {
                ReportMismatchedDomain(context, stage, action, domain, "Action", action.Name);
            }
        }

        foreach (var property in entity.Properties.Concat(entity.Events.SelectMany(static @event => @event.Properties))) {
            foreach (var policy in property.Policies) {
                ReportMismatchedDomain(context, property, policy, domain, "Policy", policy.Name);
            }
        }
    }

    private static void ValidateRelationshipEndpoints(AnalysisContext context, Domain domain) {
        foreach (var relationship in domain.Relationships.Where(context.ShouldAnalyze)) {
            if (relationship.Source is null || relationship.Target is null) {
                context.ReportError(
                    relationship,
                    $"Relationship '{relationship.Name}' must have both source and target defined.",
                    DomainModelDiagnosticCodes.MutationInvariant);
                continue;
            }

            if (!ReferenceEquals(relationship.Source.Domain, domain) || !ReferenceEquals(relationship.Target.Domain, domain)) {
                context.ReportError(
                    relationship,
                    $"Relationship '{relationship.Name}' source and target must belong to domain '{domain.Name}'.",
                    DomainModelDiagnosticCodes.MutationInvariant);
            }
        }
    }

    private static void ReportDuplicateNames<TNode>(
        AnalysisContext context,
        Node owner,
        IEnumerable<TNode> items,
        Func<TNode, string> keySelector,
        string label)
        where TNode : Node {
        foreach (var group in items.GroupBy(keySelector, StringComparer.Ordinal).Where(static group => group.Count() > 1)) {
            foreach (var duplicate in group) {
                context.ReportError(
                    duplicate,
                    $"Duplicate {label} '{group.Key}' on '{GetNodeName(owner)}'.",
                    DomainModelDiagnosticCodes.StructuralDuplicate);
            }
        }
    }

    private static void ValidateParentCycle(AnalysisContext context, Entity entity) {
        var visited = new HashSet<Entity> { entity };

        for (var current = entity.ParentEntity; current is not null; current = current.ParentEntity) {
            if (!visited.Add(current)) {
                context.ReportError(
                    entity,
                    $"Entity '{entity.Name}' participates in an inheritance cycle.",
                    DomainModelDiagnosticCodes.StructuralCycle);
                return;
            }
        }
    }

    private static void ValidateOwnershipCardinality(AnalysisContext context, Domain domain) {
        foreach (var relationship in domain.Relationships.Where(context.ShouldAnalyze).Where(static relationship => relationship.SourceOwnsTarget)) {
            if (relationship.Source is not Entity || relationship.Target is not Entity) {
                context.ReportError(
                    relationship,
                    $"Ownership relationship '{relationship.Name}' requires entity source and entity target.",
                    DomainModelDiagnosticCodes.StructuralOwnership);
            }

            if (relationship.Cardinality is RelationshipCardinality.ManyToOne or RelationshipCardinality.ManyToMany) {
                context.ReportError(
                    relationship,
                    $"Ownership relationship '{relationship.Name}' must be one-to-one or one-to-many.",
                    DomainModelDiagnosticCodes.StructuralOwnership);
            }
        }
    }

    private static void ValidateOwnershipTargetUniqueness(AnalysisContext context, Domain domain) {
        var duplicateOwnershipTargets = domain.Relationships
            .Where(context.ShouldAnalyze)
            .Where(static relationship => relationship.SourceOwnsTarget && relationship.Target is not null)
            .GroupBy(static relationship => relationship.Target)
            .Where(static group => group.Key is not null && group.Count() > 1);

        foreach (var group in duplicateOwnershipTargets) {
            foreach (var relationship in group) {
                context.ReportError(
                    relationship,
                    $"Target '{group.Key.Name}' has multiple ownership relationships.",
                    DomainModelDiagnosticCodes.StructuralOwnership);
            }
        }
    }

    private static string GetNodeName(Node node) {
        return node switch {
            Relationship relationship => relationship.Name,
            Entity entity => entity.Name,
            Stage stage => stage.Name,
            Action action => action.Name,
            Event @event => @event.Name,
            Policy policy => policy.Name,
            Property property => property.Name,
            _ => node.GetType().Name
        };
    }
}