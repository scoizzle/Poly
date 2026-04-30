using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling;

public sealed record StageTransitionRequirementAnalysis(
    IReadOnlyCollection<Property> CurrentRequiredProperties,
    IReadOnlyCollection<Property> TargetRequiredProperties,
    IReadOnlyCollection<Property> NewlyRequiredProperties);

internal sealed record RequiredPropertiesAnalysisMetadata(IReadOnlyCollection<Property> Properties) : IAnalysisMetadata;
internal sealed record StageTransitionRequirementAnalysisMetadata(StageTransitionRequirementAnalysis Analysis) : IAnalysisMetadata;

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
        foreach (var entity in domain.Types.OfType<Entity>().Where(context.ShouldAnalyze)) {
            AnalyzeEntity(context, entity);
        }
    }

    private static void AnalyzeEntity(AnalysisContext context, Entity entity) {
        ValidateEntityPolicies(context, entity);

        var required = PolicyConstraintAnalysisHelpers.ComputeRequiredProperties(entity, stage: null);
        context.SetMetadata(entity, new RequiredPropertiesAnalysisMetadata(required));

        foreach (var stage in entity.Stages.Where(context.ShouldAnalyze)) {
            AnalyzeStage(context, stage);
        }
    }

    private static void AnalyzeStage(AnalysisContext context, Stage stage) {
        var ownerEntity = stage.OwnerEntity;
        if (ownerEntity is null || !context.ShouldAnalyze(ownerEntity)) {
            return;
        }

        var targetRequired = PolicyConstraintAnalysisHelpers.ComputeRequiredProperties(ownerEntity, stage);
        context.SetMetadata(stage, new RequiredPropertiesAnalysisMetadata(targetRequired));

        var currentRequired = stage.Parent is null
            ? Array.Empty<Property>()
            : PolicyConstraintAnalysisHelpers.ComputeRequiredProperties(ownerEntity, stage.Parent);

        var currentByName = currentRequired.ToDictionary(static property => property.Name, StringComparer.Ordinal);
        var newlyRequired = targetRequired
            .Where(property => !currentByName.ContainsKey(property.Name))
            .ToArray();

        context.SetMetadata(
            stage,
            new StageTransitionRequirementAnalysisMetadata(
                new StageTransitionRequirementAnalysis(currentRequired, targetRequired, newlyRequired)));
    }

    private static void ValidateEntityPolicies(AnalysisContext context, Entity entity) {
        if (entity is Relationship) {
            return;
        }

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

public static class PolicyConstraintAnalyzerExtensions {
    extension(AnalysisResult result) {
        public IReadOnlyCollection<Property> GetRequiredProperties(DomainObject domainObject) {
            ArgumentNullException.ThrowIfNull(domainObject);

            return result.GetMetadata<RequiredPropertiesAnalysisMetadata>(domainObject)?.Properties
                ?? throw new InvalidOperationException("Required properties were not produced for the analysis request.");
        }

        public StageTransitionRequirementAnalysis GetStageTransitionRequirements(DomainObject domainObject) {
            ArgumentNullException.ThrowIfNull(domainObject);

            return result.GetMetadata<StageTransitionRequirementAnalysisMetadata>(domainObject)?.Analysis
                ?? throw new InvalidOperationException("Stage transition requirements were not produced for the analysis request.");
        }
    }
}