using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling;

internal sealed class StructuralDomainAnalyzer : INodeAnalyzer {
    public void Analyze(AnalysisContext context, Node node) {
        if (!context.ShouldAnalyze(node)) {
            return;
        }

        var domain = ResolveDomain(node);
        if (domain is null) {
            return;
        }

        AnalyzeDomain(context, domain);
    }

    private static Domain? ResolveDomain(Node node) {
        return node switch {
            Domain request => request.Domain,
            DomainObject obj => obj.Domain,
            _ => null
        };
    }

    private static void AnalyzeEntityStandalone(AnalysisContext context, Entity entity) {
        if (!context.TryBeginAnalyzerVisit<StructuralDomainAnalyzer>(entity)) {
            return;
        }

        ReportDuplicateNames(context, entity, entity.Properties);
        ReportDuplicateNames(context, entity, entity.Stages);
        ReportDuplicateNames(context, entity, entity.Actions);
        ReportDuplicateNames(context, entity, entity.Policies);
        ReportDuplicateNames(context, entity, entity.Events);
        ReportDuplicateNames(context, entity, entity.Relationships);

        foreach (var stage in entity.Stages) {
            AnalyzeStageStandalone(context, stage);
        }

        foreach (var action in entity.Actions) {
            AnalyzeActionStandalone(context, action);
        }

        foreach (var stageAction in entity.Stages.SelectMany(static stage => stage.Actions)) {
            AnalyzeActionStandalone(context, stageAction);
        }

        foreach (var @event in entity.Events) {
            AnalyzeEventStandalone(context, @event);
        }

        foreach (var property in entity.Properties.Concat(entity.Events.SelectMany(static @event => @event.Properties))) {
            AnalyzePropertyStandalone(context, property);
        }

        ValidateEntityMembership(context, entity.Domain, entity);
        ValidateParentCycle(context, entity);
    }

    private static void AnalyzeStageStandalone(AnalysisContext context, Stage stage) {
        if (!context.TryBeginAnalyzerVisit<StructuralDomainAnalyzer>(stage)) {
            return;
        }

        ReportDuplicateNames(context, stage, stage.Policies);
        ReportDuplicateNames(context, stage, stage.Actions);

        foreach (var action in stage.Actions) {
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

        ReportDuplicateNames(context, action, action.Parameters);
    }

    private static void AnalyzeEventStandalone(AnalysisContext context, Event @event) {
        if (!context.TryBeginAnalyzerVisit<StructuralDomainAnalyzer>(@event)) {
            return;
        }

        ReportDuplicateNames(context, @event, @event.Properties);
    }

    private static void AnalyzePropertyStandalone(AnalysisContext context, Property property) {
        if (!context.TryBeginAnalyzerVisit<StructuralDomainAnalyzer>(property)) {
            return;
        }

        ReportDuplicateNames(context, property, property.Policies);
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

        ReportDuplicateNames(context, domain, domain.Objects.OfType<DomainMember>());

        ValidateDomainMembership(context, domain);
        ValidateRelationshipEndpoints(context, domain);
        ValidateOwnershipCardinality(context, domain);

        foreach (var entity in domain.Entities) {
            AnalyzeEntityStandalone(context, entity);
        }

        foreach (var relationship in domain.Relationships) {
            ValidateStandaloneRelationship(context, relationship);
        }

        foreach (var @event in domain.Types.OfType<Event>()) {
            AnalyzeEventStandalone(context, @event);

            foreach (var property in @event.Properties) {
                AnalyzePropertyStandalone(context, property);
            }
        }
    }

    private static void ValidateDomainMembership(AnalysisContext context, Domain domain) {
        foreach (var obj in domain.Objects.OfType<DomainMember>()) {
            if (!ReferenceEquals(obj.Domain, domain)) {
                var identifier = obj is DomainMember member ? member.Name : obj.Id.Value;

                context.ReportError(
                    obj,
                    $"Domain Object '{identifier}' does not belong to domain '{domain.Name}'.",
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

                if (stage.OwnerEntity is not null && !ReferenceEquals(action.Entity, stage.OwnerEntity)) {
                    context.ReportError(
                        action,
                        $"Action '{action.Name}' on stage '{stage.Name}' must belong to entity '{stage.OwnerEntity.Name}'.",
                        DomainModelDiagnosticCodes.MutationInvariant);
                }

                foreach (var parameter in action.Parameters) {
                    ReportMismatchedDomain(context, action, parameter, domain, "Parameter", parameter.Name);
                }
            }
        }

        foreach (var property in entity.Properties.Concat(entity.Events.SelectMany(static @event => @event.Properties))) {
            foreach (var policy in property.Policies) {
                ReportMismatchedDomain(context, property, policy, domain, "Policy", policy.Name);
            }
        }
    }

    private static void ValidateRelationshipEndpoints(AnalysisContext context, Domain domain) {
        foreach (var relationship in domain.Relationships) {
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
        IEnumerable<TNode> items)
        where TNode : DomainMember {
        foreach (var group in items.GroupBy(static item => item.Name, StringComparer.Ordinal).Where(static group => group.Count() > 1)) {
            foreach (var duplicate in group) {
                var label = GetNodeTypeLabel(duplicate);
                context.ReportError(
                    duplicate,
                    $"Duplicate {label} '{group.Key}' on '{GetNodeName(owner)}'." + $" {label}s must have unique names within their owner.",
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
        foreach (var relationship in domain.Relationships.Where(static relationship => relationship.SourceOwnsTarget)) {
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

    private static string GetNodeTypeLabel(DomainObject node) {
        return node switch {
            Actor => "Actor",
            Relationship => "Relationship",
            Entity => "Entity",
            Stage => "Stage",
            Action => "Action",
            Event => "Event",
            Property => "Property",
            Policy => "Policy",
            _ => node.GetType().Name
        };
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