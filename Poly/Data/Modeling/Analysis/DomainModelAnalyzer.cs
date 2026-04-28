using Poly.Data.Modeling.Effects;
using Poly.Data.Modeling.Effects.Mutations;
using Poly.Data.Modeling.TypeSystem;
using Poly.Data.Modeling.Validation;
using Poly.Data.Modeling.Validation.Constraints;

namespace Poly.Data.Modeling;

public sealed class DomainModelAnalyzer {
    private readonly Analyzer _analyzer;

    public DomainModelAnalyzer()
        : this(new AnalyzerBuilder().UseDomainModelValidation().Build()) {
    }

    internal DomainModelAnalyzer(Analyzer analyzer) {
        _analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
    }

    public AnalysisResult Analyze(Node root) => _analyzer.Analyze(root);

    public AnalysisResult AnalyzeDomain(Domain domain) {
        ArgumentNullException.ThrowIfNull(domain);

        var request = new DomainModelAnalysisRequest(domain);
        return Analyze(request);
    }

    public IReadOnlyCollection<Property> AnalyzeRequiredProperties(Entity entityType, Stage? initialStage = null) {
        ArgumentNullException.ThrowIfNull(entityType);

        var request = new RequiredPropertiesAnalysisRequest(entityType, initialStage);
        var result = Analyze(request);

        return result.GetRequiredProperties(request);
    }

    public StageTransitionRequirementAnalysis AnalyzeStageTransitionRequirements(Stage currentStage, Stage targetStage, Entity entityType) {
        ArgumentNullException.ThrowIfNull(currentStage);
        ArgumentNullException.ThrowIfNull(targetStage);
        ArgumentNullException.ThrowIfNull(entityType);

        var request = new StageTransitionRequirementAnalysisRequest(currentStage, targetStage, entityType);
        var result = Analyze(request);

        return result.GetStageTransitionRequirements(request);
    }

    public DomainImplementationModel LowerToImplementationAst(Domain domain) {
        ArgumentNullException.ThrowIfNull(domain);

        var analysis = AnalyzeDomain(domain);
        return new DomainImplementationLoweringPass().Lower(domain, analysis);
    }
}

public sealed record DomainModelAnalysisRequest(Domain Domain) : Node {
    public override IEnumerable<Node?> Children {
        get {
            foreach (var type in Domain.Types.OfType<Node>()) {
                yield return type;
            }

            foreach (var relationship in Domain.Relationships) {
                yield return relationship;
            }
        }
    }
}

public sealed record RequiredPropertiesAnalysisRequest(Entity EntityType, Stage? InitialStage) : Node {
    public override IEnumerable<Node?> Children {
        get {
            yield return EntityType;

            if (InitialStage is not null) {
                yield return InitialStage;
            }
        }
    }
}

public sealed record StageTransitionRequirementAnalysisRequest(Stage CurrentStage, Stage TargetStage, Entity EntityType) : Node {
    public override IEnumerable<Node?> Children {
        get {
            yield return CurrentStage;
            yield return TargetStage;
            yield return EntityType;
        }
    }
}

internal sealed record RequiredPropertiesAnalysisMetadata(IReadOnlyCollection<Property> Properties) : IAnalysisMetadata;

internal sealed record StageTransitionRequirementAnalysisMetadata(StageTransitionRequirementAnalysis Analysis) : IAnalysisMetadata;

internal static class DomainModelDiagnosticCodes {
    public const string StructuralDuplicate = "DMSTR001";
    public const string StructuralCycle = "DMSTR002";
    public const string StructuralOwnership = "DMSTR003";
    public const string MutationInvariant = "DMMUT001";
    public const string SemanticStageInheritance = "DMSEM001";
    public const string SemanticActionVisibility = "DMSEM002";
    public const string SemanticTypeCompatibility = "DMSEM003";
    public const string PolicyMissingProperty = "DMPOL001";
    public const string EffectBinding = "DMEFF001";
}

internal sealed class StructuralDomainAnalyzer : INodeAnalyzer {
    public void Analyze(AnalysisContext context, Node node) {
        switch (node) {
            case DomainModelAnalysisRequest request:
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
        ReportDuplicateNames(context, domain, domain.Types, static type => type.Name, "type");
        ReportDuplicateNames(context, domain, domain.Relationships, static relationship => relationship.Name, "relationship");

        foreach (var entity in domain.Types.OfType<Entity>()) {
            ReportDuplicateNames(context, entity, entity.Properties, static property => property.Name, "property");
            ReportDuplicateNames(context, entity, entity.Stages, static stage => stage.Name, "stage");
            ReportDuplicateNames(context, entity, entity.Actions, static action => action.Name, "action");
            ReportDuplicateNames(context, entity, entity.Policies, static policy => policy.Name, "policy");
            ReportDuplicateNames(context, entity, entity.Events, static @event => @event.Name, "event");
            ReportDuplicateNames(context, entity, entity.Relationships, static relationship => relationship.Name, "relationship");
            ValidateEntityMembership(context, domain, entity);

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

            ValidateParentCycle(context, entity);
        }

        ValidateDomainMembership(context, domain);
        ValidateRelationshipEndpoints(context, domain);

        ValidateOwnershipCardinality(context, domain);
        ValidateOwnershipTargetUniqueness(context, domain);
    }

    private static void ValidateDomainMembership(AnalysisContext context, Domain domain) {
        foreach (var type in domain.Types) {
            if (!ReferenceEquals(type.Domain, domain)) {
                context.ReportError(
                    type,
                    $"Type '{type.Name}' does not belong to domain '{domain.Name}'.",
                    DomainModelDiagnosticCodes.MutationInvariant);
            }
        }

        foreach (var relationship in domain.Relationships) {
            if (!ReferenceEquals(relationship.Domain, domain)) {
                context.ReportError(
                    relationship,
                    $"Relationship '{relationship.Name}' does not belong to domain '{domain.Name}'.",
                    DomainModelDiagnosticCodes.MutationInvariant);
            }
        }
    }

    private static void ValidateEntityMembership(AnalysisContext context, Domain domain, Entity entity) {
        static void ReportMismatchedDomain(AnalysisContext context, Node owner, DomainObject child, Domain domain, string childLabel, string childName) {
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

    private static void ValidateOwnershipTargetUniqueness(AnalysisContext context, Domain domain) {
        var duplicateOwnershipTargets = domain.Relationships
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

internal sealed class SemanticDomainAnalyzer : INodeAnalyzer {
    public void Analyze(AnalysisContext context, Node node) {
        switch (node) {
            case DomainModelAnalysisRequest request:
                AnalyzeDomain(context, request.Domain);
                break;
            case Entity entity:
                AnalyzeEntitySemantics(context, entity);
                break;
        }

        this.AnalyzeChildren(context, node);
    }

    private static void AnalyzeDomain(AnalysisContext context, Domain domain) {
        foreach (var entity in domain.Types.OfType<Entity>()) {
            AnalyzeEntitySemantics(context, entity);
        }
    }

    private static void AnalyzeEntitySemantics(AnalysisContext context, Entity entity) {
        ValidateStageInheritance(context, entity);
        ValidateStageActionVisibility(context, entity);
        ValidateTypeCompatibility(context, entity);
    }

    private static void ValidateStageInheritance(AnalysisContext context, Entity entity) {
        if (entity.ParentEntity is null || entity.ParentEntity.Stages.Count == 0) {
            return;
        }

        foreach (var stage in entity.Stages) {
            if (stage.Parent is null) {
                context.ReportError(
                    stage,
                    $"Stage '{stage.Name}' on child entity '{entity.Name}' must have a parent stage when parent entity '{entity.ParentEntity.Name}' defines stages.",
                    DomainModelDiagnosticCodes.SemanticStageInheritance);
                continue;
            }

            if (!entity.ParentEntity.Stages.Contains(stage.Parent)) {
                context.ReportError(
                    stage,
                    $"Stage '{stage.Name}' on child entity '{entity.Name}' must directly inherit from a stage defined on parent entity '{entity.ParentEntity.Name}'.",
                    DomainModelDiagnosticCodes.SemanticStageInheritance);
            }
        }
    }

    private static void ValidateStageActionVisibility(AnalysisContext context, Entity entity) {
        foreach (var stage in entity.Stages) {
            foreach (var action in stage.Actions) {
                if (!ReferenceEquals(action.Entity, entity)) {
                    context.ReportError(
                        action,
                        $"Action '{action.Name}' on stage '{stage.Name}' must belong to entity '{entity.Name}'.",
                        DomainModelDiagnosticCodes.SemanticActionVisibility);
                }
            }
        }
    }

    private static void ValidateTypeCompatibility(AnalysisContext context, Entity entity) {
        foreach (var property in entity.Properties) {
            if (!ReferenceEquals(property.Type.Domain, entity.Domain)) {
                context.ReportError(
                    property,
                    $"Property '{property.Name}' uses type '{property.Type.Name}' from a different domain.",
                    DomainModelDiagnosticCodes.SemanticTypeCompatibility);
            }
        }

        foreach (var action in entity.Actions) {
            foreach (var parameter in action.Parameters.OfType<Property>()) {
                if (!ReferenceEquals(parameter.Type.Domain, entity.Domain)) {
                    context.ReportError(
                        parameter,
                        $"Action '{action.Name}' parameter '{parameter.Name}' uses a type from a different domain.",
                        DomainModelDiagnosticCodes.SemanticTypeCompatibility);
                }
            }
        }
    }
}

internal sealed class PolicyConstraintAnalyzer : INodeAnalyzer {
    public void Analyze(AnalysisContext context, Node node) {
        switch (node) {
            case RequiredPropertiesAnalysisRequest requiredPropertiesRequest:
                context.SetMetadata(
                    requiredPropertiesRequest,
                    new RequiredPropertiesAnalysisMetadata(
                        PolicyConstraintAnalysisHelpers.ComputeRequiredProperties(requiredPropertiesRequest.EntityType, requiredPropertiesRequest.InitialStage)));
                break;
            case StageTransitionRequirementAnalysisRequest transitionRequest:
                var currentRequired = PolicyConstraintAnalysisHelpers.ComputeRequiredProperties(transitionRequest.EntityType, transitionRequest.CurrentStage);
                var targetRequired = PolicyConstraintAnalysisHelpers.ComputeRequiredProperties(transitionRequest.EntityType, transitionRequest.TargetStage);
                var currentByName = currentRequired.ToDictionary(property => property.Name, StringComparer.Ordinal);
                var newlyRequired = targetRequired
                    .Where(property => !currentByName.ContainsKey(property.Name))
                    .ToArray();

                context.SetMetadata(
                    transitionRequest,
                    new StageTransitionRequirementAnalysisMetadata(
                        new StageTransitionRequirementAnalysis(currentRequired, targetRequired, newlyRequired)));
                break;

            case DomainModelAnalysisRequest domainRequest:
                ValidateDomainPolicies(context, domainRequest.Domain);
                break;
        }

        this.AnalyzeChildren(context, node);
    }

    private static void ValidateDomainPolicies(AnalysisContext context, Domain domain) {
        foreach (var entity in domain.Types.OfType<Entity>()) {
            var propertyNames = entity.Properties
                .Select(static property => property.Name)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var policy in entity.Policies.Concat(entity.Properties.SelectMany(static property => property.Policies))) {
                foreach (var rule in policy.Rules.OfType<Rule>()) {
                    if (rule.Value is not Property property) {
                        continue;
                    }

                    if (!propertyNames.Contains(property.Name)) {
                        context.ReportError(
                            policy,
                            $"Policy '{policy.Name}' on entity '{entity.Name}' references property '{property.Name}' that is not defined on the entity.",
                            DomainModelDiagnosticCodes.PolicyMissingProperty);
                    }
                }
            }
        }
    }
}

internal sealed class EffectBindingAnalyzer : INodeAnalyzer {
    public void Analyze(AnalysisContext context, Node node) {
        switch (node) {
            case DomainModelAnalysisRequest request:
                AnalyzeDomain(context, request.Domain);
                break;
            case Action action:
                ValidateActionEffects(context, action.Entity, action);
                break;
        }

        this.AnalyzeChildren(context, node);
    }

    private static void AnalyzeDomain(AnalysisContext context, Domain domain) {
        foreach (var entity in domain.Types.OfType<Entity>()) {
            foreach (var action in entity.Actions.Concat(entity.Stages.SelectMany(static stage => stage.Actions))) {
                ValidateActionEffects(context, entity, action);
            }
        }
    }

    private static void ValidateActionEffects(AnalysisContext context, Entity ownerEntity, Action action) {
        foreach (var effect in action.Effects) {
            ValidateEffect(context, ownerEntity, action, effect);
        }
    }

    private static void ValidateEffect(AnalysisContext context, Entity ownerEntity, Action action, Effect effect) {
        try {
            effect.Validate(ownerEntity);
        }
        catch (InvalidOperationException ex) {
            context.ReportError(
                action,
                $"Action '{action.Name}' has invalid effect '{effect.GetType().Name}': {ex.Message}",
                DomainModelDiagnosticCodes.EffectBinding);
            return;
        }

        switch (effect) {
            case PublishEvent publishEvent:
                ValidatePublishEventBindings(context, action, publishEvent);
                break;
            case InvokeAction invokeAction:
                ValidateInvokeActionBindings(context, action, invokeAction);
                break;
            case CreateEntityInstance createEntityInstance:
                ValidateCreateBindings(context, action, createEntityInstance);
                break;
            case StageTransition:
            case Assign:
                break;
        }
    }

    private static void ValidatePublishEventBindings(AnalysisContext context, Action action, PublishEvent publishEvent) {
        foreach (var eventProperty in publishEvent.Event.Properties) {
            if (!publishEvent.HasBindingFor(eventProperty)) {
                context.ReportError(
                    action,
                    $"PublishEvent for '{publishEvent.Event.Name}' is missing binding for '{eventProperty.Name}'.",
                    DomainModelDiagnosticCodes.EffectBinding);
            }
        }
    }

    private static void ValidateInvokeActionBindings(AnalysisContext context, Action action, InvokeAction invokeAction) {
        foreach (var targetParameter in invokeAction.TargetAction.Parameters.OfType<Property>()) {
            if (!invokeAction.HasBindingFor(targetParameter)) {
                context.ReportError(
                    action,
                    $"InvokeAction for '{invokeAction.TargetAction.Name}' is missing binding for '{targetParameter.Name}'.",
                    DomainModelDiagnosticCodes.EffectBinding);
            }
        }
    }

    private static void ValidateCreateBindings(AnalysisContext context, Action action, CreateEntityInstance createEntityInstance) {
        var required = createEntityInstance.GetRequiredProperties();
        foreach (var requiredProperty in required) {
            if (!action.Parameters.OfType<Property>().Any(parameter => string.Equals(parameter.Name, requiredProperty.Name, StringComparison.Ordinal))) {
                context.ReportWarning(
                    action,
                    $"CreateEntityInstance may require '{requiredProperty.Name}', but action '{action.Name}' has no matching parameter.",
                    DomainModelDiagnosticCodes.EffectBinding);
            }
        }
    }
}

internal static class PolicyConstraintAnalysisHelpers {
    public static IReadOnlyCollection<Property> ComputeRequiredProperties(Entity entityType, Stage? stage) {
        var entityProperties = entityType.Properties.ToDictionary(property => property.Name, StringComparer.Ordinal);
        var requiredPropertiesByName = new Dictionary<string, Property>(StringComparer.Ordinal);

        foreach (var property in entityProperties.Values) {
            if (property.Constraints.Any(constraint => constraint.IsOrContains<RequiredConstraint>())) {
                requiredPropertiesByName[property.Name] = property;
            }
        }

        foreach (var policy in EnumerateEffectivePolicies(entityType, stage)) {
            foreach (var rule in policy.Rules.OfType<Rule>()) {
                if (rule.Value is not Property policyProperty) {
                    continue;
                }

                if (!entityProperties.TryGetValue(policyProperty.Name, out var entityProperty)) {
                    continue;
                }

                if (rule.Constraints.IsOrContains<RequiredConstraint>()) {
                    requiredPropertiesByName[entityProperty.Name] = entityProperty;
                }
            }
        }

        return requiredPropertiesByName.Values.ToArray();
    }

    private static IEnumerable<Policy> EnumerateEffectivePolicies(Entity entityType, Stage? stage) {
        var policies = new Dictionary<string, Policy>(StringComparer.Ordinal);

        foreach (var policy in entityType.Policies) {
            _ = policies.TryAdd(policy.Name, policy);
        }

        foreach (var property in entityType.Properties) {
            foreach (var policy in property.Policies) {
                _ = policies.TryAdd(policy.Name, policy);
            }
        }

        for (var currentStage = stage; currentStage is not null; currentStage = currentStage.Parent) {
            foreach (var policy in currentStage.Policies) {
                _ = policies.TryAdd(policy.Name, policy);
            }
        }

        return policies.Values;
    }
}

public static class DomainModelAnalysisBuilderExtensions {
    extension(AnalyzerBuilder builder) {
        public AnalyzerBuilder UseDomainModelAnalysisPipeline() {
            builder.AddAnalyzer(new StructuralDomainAnalyzer());
            builder.AddAnalyzer(new SemanticDomainAnalyzer());
            builder.AddAnalyzer(new PolicyConstraintAnalyzer());
            builder.AddAnalyzer(new EffectBindingAnalyzer());
            return builder;
        }

        public AnalyzerBuilder UseDomainModelValidation() {
            return builder.UseDomainModelAnalysisPipeline();
        }
    }

    extension(AnalysisResult result) {
        public IReadOnlyCollection<Property> GetRequiredProperties(RequiredPropertiesAnalysisRequest request) {
            ArgumentNullException.ThrowIfNull(request);

            return result.GetMetadata<RequiredPropertiesAnalysisMetadata>(request)?.Properties
                ?? throw new InvalidOperationException("Required properties were not produced for the analysis request.");
        }

        public StageTransitionRequirementAnalysis GetStageTransitionRequirements(StageTransitionRequirementAnalysisRequest request) {
            ArgumentNullException.ThrowIfNull(request);

            return result.GetMetadata<StageTransitionRequirementAnalysisMetadata>(request)?.Analysis
                ?? throw new InvalidOperationException("Stage transition requirements were not produced for the analysis request.");
        }
    }
}