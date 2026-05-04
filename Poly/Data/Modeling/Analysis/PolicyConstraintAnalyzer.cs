using Poly.Data.Modeling.TypeSystem;
using Poly.Data.Modeling.Validation;
using Poly.Data.Modeling.Validation.Constraints;

namespace Poly.Data.Modeling;

public sealed record StageTransitionRequirementAnalysis(
    IReadOnlyCollection<Property> CurrentRequiredProperties,
    IReadOnlyCollection<Property> TargetRequiredProperties,
    IReadOnlyCollection<Property> NewlyRequiredProperties);

internal sealed record RequiredPropertiesAnalysisMetadata(IReadOnlyCollection<Property> Properties) : IAnalysisMetadata;
internal sealed record StageTransitionRequirementAnalysisMetadata(StageTransitionRequirementAnalysis Analysis) : IAnalysisMetadata;

internal static class PolicyConstraintHelpers {
    public static IReadOnlyCollection<Property> ComputeRequiredProperties(Entity entityType, Stage? stage) {
        var entityProperties = entityType.Properties
            .GroupBy(property => property.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);
        var required = new Dictionary<string, Property>(StringComparer.Ordinal);

        foreach (var property in entityProperties.Values) {
            if (property.Constraints.Any(static c => c.IsOrContains<RequiredConstraint>())) {
                required[property.Name] = property;
            }
        }

        foreach (var policy in entityType.Policies) {
            CollectRequiredFromPolicy(policy, entityProperties, required);
        }

        foreach (var property in entityType.Properties) {
            foreach (var policy in property.Policies) {
                CollectRequiredFromPolicy(policy, entityProperties, required);
            }
        }

        for (var current = stage; current is not null; current = current.Parent) {
            foreach (var policy in current.Policies) {
                CollectRequiredFromPolicy(policy, entityProperties, required);
            }
        }

        return required.Values.ToArray();
    }

    private static void CollectRequiredFromPolicy(Policy policy, Dictionary<string, Property> entityProperties, Dictionary<string, Property> required) {
        foreach (var rule in policy.Rules.OfType<PropertyRule>()) {
            if (rule.Value is not Property policyProperty) {
                continue;
            }

            if (!entityProperties.TryGetValue(policyProperty.Name, out var entityProperty)) {
                continue;
            }

            if (rule.Constraints.IsOrContains<RequiredConstraint>()) {
                required[entityProperty.Name] = entityProperty;
            }
        }
    }
}

internal sealed class PolicyConstraintAnalyzer : INodeAnalyzer {
    public void Analyze(AnalysisContext context, Node node) {
        if (!context.ShouldAnalyze(node)) {
            return;
        }

        switch (node) {
            case Domain request:
                AnalyzeDomain(context, request.Domain);
                break;
            case Entity entity:
                AnalyzeEntity(context, entity);
                break;
            case Stage stage:
                AnalyzeStage(context, stage);
                break;
        }

        this.AnalyzeChildren(context, node);
    }

    private static void AnalyzeDomain(AnalysisContext context, Domain domain) {
        if (!context.TryBeginAnalyzerVisit<PolicyConstraintAnalyzer>(domain)) {
            return;
        }

        foreach (var entity in domain.Types.OfType<Entity>().Where(context.ShouldAnalyze)) {
            AnalyzeEntity(context, entity);
        }
    }

    private static void AnalyzeEntity(AnalysisContext context, Entity entity) {
        if (!context.TryBeginAnalyzerVisit<PolicyConstraintAnalyzer>(entity)) {
            return;
        }

        ValidateEntityPolicies(context, entity);

        var required = PolicyConstraintHelpers.ComputeRequiredProperties(entity, stage: null);
        context.SetMetadata(entity, new RequiredPropertiesAnalysisMetadata(required));

        foreach (var property in entity.Properties.Where(context.ShouldAnalyze)) {
            var validationAst = BuildPropertyValidationAst(context, property);
            if (validationAst is not null) {
                context.SetMetadata(property, new PropertyValidationAstMetadata(validationAst));
            }
        }

        foreach (var stage in entity.Stages.Where(context.ShouldAnalyze)) {
            AnalyzeStage(context, stage);
        }
    }

    private static void AnalyzeStage(AnalysisContext context, Stage stage) {
        if (!context.TryBeginAnalyzerVisit<PolicyConstraintAnalyzer>(stage)) {
            return;
        }

        var ownerEntity = stage.OwnerEntity;
        if (ownerEntity is null || !context.ShouldAnalyze(ownerEntity)) {
            return;
        }

        var targetRequired = PolicyConstraintHelpers.ComputeRequiredProperties(ownerEntity, stage);
        context.SetMetadata(stage, new RequiredPropertiesAnalysisMetadata(targetRequired));

        IReadOnlyCollection<Property> currentRequired = stage.Parent is not null && context.ShouldAnalyze(stage.Parent)
            ? context.GetMetadata<RequiredPropertiesAnalysisMetadata>(stage.Parent)?.Properties ?? Array.Empty<Property>()
            : context.GetMetadata<RequiredPropertiesAnalysisMetadata>(ownerEntity)?.Properties ?? Array.Empty<Property>();

        var currentByName = currentRequired.ToDictionary(static property => property.Name, StringComparer.Ordinal);
        var newlyRequired = targetRequired
            .Where(property => !currentByName.ContainsKey(property.Name))
            .ToArray();

        var analysis = new StageTransitionRequirementAnalysis(currentRequired, targetRequired, newlyRequired);
        context.SetMetadata(
            stage,
            new StageTransitionRequirementAnalysisMetadata(analysis));

        var transitionGuard = BuildTransitionValidationAst(context, stage, newlyRequired);
        if (transitionGuard is not null) {
            context.SetMetadata(stage, new TransitionValidationAstMetadata(transitionGuard));
        }
    }

    private static void ValidateEntityPolicies(AnalysisContext context, Entity entity) {
        if (entity is Relationship) {
            return;
        }

        var propertyNames = entity.Properties
            .Select(static property => property.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var policy in entity.Policies.Concat(entity.Properties.SelectMany(static property => property.Policies))) {
            foreach (var rule in policy.Rules.OfType<PropertyRule>()) {
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

    private static Node? BuildPropertyValidationAst(AnalysisContext context, Property property) {
        var constraints = property.Constraints;
        if (constraints.Count == 0) {
            return null;
        }

        var subject = new Variable(property.Name, null);

        try {
            var constraintSet = new ConstraintSet(ConstraintAggregationStrategy.All, constraints);
            return DomainLoweringGenerator.LowerConstraint(constraintSet, subject);
        }
        catch (Exception ex) {
            context.ReportError(
                property,
                $"Failed to build validation AST for property '{property.Name}': {ex.Message}",
                DomainModelDiagnosticCodes.PolicyAstGeneration);
            return null;
        }
    }

    private static Node? BuildTransitionValidationAst(AnalysisContext context, Stage stage, IReadOnlyCollection<Property> newlyRequired) {
        if (newlyRequired.Count == 0) {
            return True;
        }

        var ownerEntity = stage.OwnerEntity;
        if (ownerEntity is null) {
            return null;
        }

        Node combinedCheck = True;

        foreach (var property in newlyRequired) {
            var validationAst = context.GetMetadata<PropertyValidationAstMetadata>(property)?.ValidationAst;
            if (validationAst is not null) {
                combinedCheck = new And(combinedCheck, validationAst);
            }
            else {
                var subject = new Variable(property.Name, null);
                combinedCheck = new And(combinedCheck, new NotEqual(subject, Null));
            }
        }

        return combinedCheck;
    }
}

public static class PolicyConstraintAnalyzerExtensions {
    extension(AnalysisResult result) {
        public IReadOnlyCollection<Property> GetRequiredProperties(DomainMember domainObject) {
            ArgumentNullException.ThrowIfNull(domainObject);

            return result.GetMetadata<RequiredPropertiesAnalysisMetadata>(domainObject)?.Properties
                ?? throw new InvalidOperationException("Required properties were not produced for the analysis request.");
        }

        public StageTransitionRequirementAnalysis GetStageTransitionRequirements(DomainMember domainObject) {
            ArgumentNullException.ThrowIfNull(domainObject);

            return result.GetMetadata<StageTransitionRequirementAnalysisMetadata>(domainObject)?.Analysis
                ?? throw new InvalidOperationException("Stage transition requirements were not produced for the analysis request.");
        }

        public Node? GetPropertyValidationAst(Property property) {
            ArgumentNullException.ThrowIfNull(property);

            return result.GetMetadata<PropertyValidationAstMetadata>(property)?.ValidationAst;
        }

        public Node? GetTransitionValidationAst(Stage stage) {
            ArgumentNullException.ThrowIfNull(stage);

            return result.GetMetadata<TransitionValidationAstMetadata>(stage)?.TransitionGuardAst;
        }
    }
}