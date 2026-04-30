using Poly.Data.Modeling.Validation;
using Poly.Data.Modeling.Validation.Constraints;

namespace Poly.Data.Modeling;

internal static class PolicyConstraintAnalysisHelpers {
    public static IReadOnlyCollection<Property> ComputeRequiredProperties(Entity entityType, Stage? stage) {
        // Allow duplicate property names; use the last property with a given name
        var entityProperties = entityType.Properties
            .GroupBy(property => property.Name, StringComparer.Ordinal)
            .Select(group => group.Last())
            .ToDictionary(property => property.Name, StringComparer.Ordinal);
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