using Poly.Data.Modeling.Validation;
using Poly.Data.Modeling.Validation.Constraints;

namespace Poly.Data.Modeling;

public sealed record StageTransitionRequirementAnalysis(
    IReadOnlyCollection<Property> CurrentRequiredProperties,
    IReadOnlyCollection<Property> TargetRequiredProperties,
    IReadOnlyCollection<Property> NewlyRequiredProperties);

public static class StageTransitionRequirementAnalyzer {
    public static StageTransitionRequirementAnalysis Analyze(Stage currentStage, Stage targetStage, Entity entityType) {
        ArgumentNullException.ThrowIfNull(currentStage);
        ArgumentNullException.ThrowIfNull(targetStage);
        ArgumentNullException.ThrowIfNull(entityType);

        var currentRequired = GetRequiredProperties(currentStage, entityType);
        var targetRequired = GetRequiredProperties(targetStage, entityType);

        var currentByName = currentRequired.ToDictionary(property => property.Name, StringComparer.Ordinal);
        var newlyRequired = targetRequired
            .Where(property => !currentByName.ContainsKey(property.Name))
            .ToArray();

        return new StageTransitionRequirementAnalysis(
            CurrentRequiredProperties: currentRequired,
            TargetRequiredProperties: targetRequired,
            NewlyRequiredProperties: newlyRequired);
    }

    private static IReadOnlyCollection<Property> GetRequiredProperties(Stage stage, Entity entityType) {
        var entityPropertiesByName = entityType.Properties.ToDictionary(property => property.Name, StringComparer.Ordinal);
        var requiredByName = new Dictionary<string, Property>(StringComparer.Ordinal);

        foreach (var policy in EnumerateEffectivePolicies(stage, entityType)) {
            foreach (var rule in policy.Rules.OfType<Rule>()) {
                if (rule.Value is not Property policyProperty) {
                    continue;
                }

                if (!entityPropertiesByName.TryGetValue(policyProperty.Name, out var entityProperty)) {
                    continue;
                }

                if (rule.Constraints.IsOrContains<RequiredConstraint>()) {
                    requiredByName[entityProperty.Name] = entityProperty;
                }
            }
        }

        return requiredByName.Values.ToArray();
    }

    private static IEnumerable<Policy> EnumerateEffectivePolicies(Stage stage, Entity entityType) {
        var policies = new Dictionary<string, Policy>(StringComparer.Ordinal);

        foreach (var policy in entityType.Policies) {
            _ = policies.TryAdd(policy.Name, policy);
        }

        foreach (var property in entityType.Properties) {
            foreach (var policy in property.Policies) {
                _ = policies.TryAdd(policy.Name, policy);
            }
        }

        foreach (var policy in stage.GetEffectivePolicies()) {
            _ = policies.TryAdd(policy.Name, policy);
        }

        return policies.Values;
    }
}