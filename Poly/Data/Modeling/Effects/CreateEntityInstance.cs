using Poly.Data.Modeling.TypeSystem;
using Poly.Data.Modeling.Validation;
using Poly.Data.Modeling.Validation.Constraints;

namespace Poly.Data.Modeling.Effects;

public sealed class CreateEntityInstance : Effect {
    public required Entity EntityType { get; init; }
    public required Relationship OwnershipRelationship { get; init; }
    public Stage? InitialStage { get; init; }

    public override IReadOnlyCollection<IDomainValue> RequiredParameters => GetRequiredProperties().Cast<IDomainValue>().ToArray();

    public IReadOnlyCollection<Property> GetRequiredProperties() {
        var entityProperties = EntityType.Properties.ToDictionary(property => property.Name, StringComparer.Ordinal);
        var requiredPropertiesByName = new Dictionary<string, Property>(StringComparer.Ordinal);

        foreach (var property in entityProperties.Values) {
            if (property.Constraints.Any(constraint => constraint.IsOrContains<RequiredConstraint>())) {
                requiredPropertiesByName[property.Name] = property;
            }
        }

        foreach (var stage in EnumerateInitialStageLineage()) {
            foreach (var policy in stage.Policies) {
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
        }

        return requiredPropertiesByName.Values.ToArray();
    }

    private IEnumerable<Stage> EnumerateInitialStageLineage() {
        for (var current = InitialStage; current is not null; current = current.Parent) {
            yield return current;
        }
    }
}