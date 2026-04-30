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

        // switch (node) {
        //     case RequiredPropertiesAnalysisRequest requiredPropertiesRequest:
        //         context.SetMetadata(
        //             requiredPropertiesRequest,
        //             new RequiredPropertiesAnalysisMetadata(
        //                 PolicyConstraintAnalysisHelpers.ComputeRequiredProperties(requiredPropertiesRequest.EntityType, requiredPropertiesRequest.InitialStage)));
        //         break;
        //     case StageTransitionRequirementAnalysisRequest transitionRequest:
        //         var currentRequired = PolicyConstraintAnalysisHelpers.ComputeRequiredProperties(transitionRequest.EntityType, transitionRequest.CurrentStage);
        //         var targetRequired = PolicyConstraintAnalysisHelpers.ComputeRequiredProperties(transitionRequest.EntityType, transitionRequest.TargetStage);
        //         var currentByName = currentRequired.ToDictionary(property => property.Name, StringComparer.Ordinal);
        //         var newlyRequired = targetRequired
        //             .Where(property => !currentByName.ContainsKey(property.Name))
        //             .ToArray();

        //         context.SetMetadata(
        //             transitionRequest,
        //             new StageTransitionRequirementAnalysisMetadata(
        //                 new StageTransitionRequirementAnalysis(currentRequired, targetRequired, newlyRequired)));
        //         break;

        //     case Domain domainRequest:
        //         ValidateDomainPolicies(context, domainRequest.Domain);
        //         break;
        // }

        this.AnalyzeChildren(context, node);
    }

    private static void ValidateDomainPolicies(AnalysisContext context, Domain domain) {
        foreach (var entity in domain.Types.OfType<Entity>().Where(context.ShouldAnalyze)) {
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