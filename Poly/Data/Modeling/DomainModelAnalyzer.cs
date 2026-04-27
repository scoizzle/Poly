using Poly.Data.Modeling.Effects;
using Poly.Data.Modeling.Effects.Mutations;
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
    public const string SemanticStageInheritance = "DMSEM001";
    public const string SemanticActionVisibility = "DMSEM002";
    public const string SemanticTypeCompatibility = "DMSEM003";
    public const string PolicyMissingProperty = "DMPOL001";
    public const string EffectBinding = "DMEFF001";
}

internal sealed class StructuralDomainAnalyzer : INodeAnalyzer {
    public void Analyze(AnalysisContext context, Node node) {
        if (node is DomainModelAnalysisRequest request) {
            AnalyzeDomain(context, request.Domain);
        }

        this.AnalyzeChildren(context, node);
    }

    private static void AnalyzeDomain(AnalysisContext context, Domain domain) {
        foreach (var entity in domain.Types.OfType<Entity>()) {
            ReportDuplicateNames(context, entity, entity.Properties, static property => property.Name, "property");
            ReportDuplicateNames(context, entity, entity.Stages, static stage => stage.Name, "stage");
            ReportDuplicateNames(context, entity, entity.Actions, static action => action.Name, "action");
            ReportDuplicateNames(context, entity, entity.Policies, static policy => policy.Name, "policy");
            ReportDuplicateNames(context, entity, entity.Events, static @event => @event.Name, "event");
            ReportDuplicateNames(context, entity, entity.Relationships, static relationship => relationship.Name, "relationship");
            ValidateParentCycle(context, entity);
        }

        ValidateOwnershipCardinality(context, domain);
        ValidateOwnershipTargetUniqueness(context, domain);
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
        if (node is DomainModelAnalysisRequest request) {
            AnalyzeDomain(context, request.Domain);
        }

        this.AnalyzeChildren(context, node);
    }

    private static void AnalyzeDomain(AnalysisContext context, Domain domain) {
        foreach (var entity in domain.Types.OfType<Entity>()) {
            ValidateStageInheritance(context, entity);
            ValidateStageActionVisibility(context, entity);
            ValidateTypeCompatibility(context, entity);
        }
    }

    private static void ValidateStageInheritance(AnalysisContext context, Entity entity) {
        if (entity.ParentEntity is null || entity.ParentEntity.Stages.Count == 0) {
            return;
        }

        foreach (var stage in entity.Stages) {
            if (stage.Parent is null) {
                context.ReportError(
                    stage,
                    $"Stage '{stage.Name}' on child entity '{entity.Name}' must define a parent stage.",
                    DomainModelDiagnosticCodes.SemanticStageInheritance);
                continue;
            }

            if (!entity.ParentEntity.Stages.Contains(stage.Parent)) {
                context.ReportError(
                    stage,
                    $"Stage '{stage.Name}' on child entity '{entity.Name}' must inherit from a stage on '{entity.ParentEntity.Name}'.",
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
        if (node is DomainModelAnalysisRequest request) {
            AnalyzeDomain(context, request.Domain);
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