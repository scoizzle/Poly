using Poly.Data.Modeling.TypeSystem;
using Poly.Data.Modeling.Validation.Constraints;

namespace Poly.Data.Modeling;

internal sealed class SemanticDomainAnalyzer : INodeAnalyzer {
    public void Analyze(AnalysisContext context, Node node) {
        if (!context.ShouldAnalyze(node)) {
            return;
        }

        switch (node) {
            case Domain request:
                AnalyzeDomain(context, request.Domain);
                break;
            case Entity entity:
                AnalyzeEntitySemantics(context, entity);
                break;
        }

        this.AnalyzeChildren(context, node);
    }

    private static void AnalyzeDomain(AnalysisContext context, Domain domain) {
        ValidateDomainTypeModelingRules(context, domain);

        foreach (var entity in domain.Types.OfType<Entity>().Where(context.ShouldAnalyze)) {
            AnalyzeEntitySemantics(context, entity);
        }
    }

    private static void ValidateDomainTypeModelingRules(AnalysisContext context, Domain domain) {
        foreach (var primitive in domain.Types.OfType<Primitive>().Where(context.ShouldAnalyze)) {
            ValidatePrimitiveCategoryModelingRules(context, primitive);
        }
    }

    private static void ValidatePrimitiveCategoryModelingRules(AnalysisContext context, Primitive primitive) {
        if (primitive.Category.Is(TypeCategory.Nullable)) {
            context.ReportError(
                primitive,
                $"Primitive '{primitive.Name}' must not use TypeCategory.Nullable. Domain nullability is modeled by RequiredConstraint.",
                DomainModelDiagnosticCodes.SemanticTypeCompatibility);
        }

        if (primitive.Category.Is(TypeCategory.Collection) || primitive.Category.Is(TypeCategory.Keyed)) {
            context.ReportError(
                primitive,
                $"Primitive '{primitive.Name}' must not use collection categories. Domain multiplicity is modeled through relationships.",
                DomainModelDiagnosticCodes.SemanticTypeCompatibility);
        }
    }

    private static void AnalyzeEntitySemantics(AnalysisContext context, Entity entity) {
        ValidateStageInheritance(context, entity);
        ValidateStageActionVisibility(context, entity);
        ValidateTypeCompatibility(context, entity);
        ValidateDiscriminatorConstraints(context, entity);

        // Duplicate property name validation
        var propertyGroups = entity.Properties.GroupBy(p => p.Name, StringComparer.Ordinal);
        foreach (var group in propertyGroups) {
            if (group.Count() > 1) {
                foreach (var property in group) {
                    context.ReportError(
                        property,
                        $"Duplicate property name '{property.Name}' found on entity '{entity.Name}'.",
                        DomainModelDiagnosticCodes.SemanticTypeCompatibility // Use or define a more specific code if desired
                    );
                }
            }
        }

        // Compute effective members (properties, actions, policies, events, relationships, stages)
        var lineage = EnumerateEntityLineageRootToLeaf(entity).ToArray();
        var effectiveProperties = MergeByName(lineage.SelectMany(static current => current.Properties), static property => property.Name);
        var effectiveActions = MergeByName(lineage.SelectMany(static current => current.Actions), static action => action.Name);
        var effectivePolicies = MergeByName(lineage.SelectMany(static current => current.Policies), static policy => policy.Name);
        var effectiveEvents = MergeByName(lineage.SelectMany(static current => current.Events), static @event => @event.Name);
        var effectiveRelationships = MergeByName(lineage.SelectMany(static current => current.Relationships), static relationship => relationship.Name);
        var effectiveStages = MergeByName(lineage.SelectMany(static current => current.Stages), static stage => stage.Name);

        context.Metadata.Set(entity, new EffectiveMemberMetadata {
            EffectiveProperties = effectiveProperties,
            EffectiveActions = effectiveActions,
            EffectivePolicies = effectivePolicies,
            EffectiveEvents = effectiveEvents,
            EffectiveRelationships = effectiveRelationships,
            EffectiveStages = effectiveStages
        });

        foreach (var stage in entity.Stages) {
            var effectiveStageActions = MergeByName(
                EnumerateStageLineageRootToLeaf(stage).SelectMany(static s => s.Actions),
                static action => action.Name);
            var effectiveStagePolicies = MergeByName(
                EnumerateStageLineageRootToLeaf(stage).SelectMany(static s => s.Policies),
                static policy => policy.Name);

            context.Metadata.Set(stage, new EffectiveStageMetadata {
                EffectiveActions = effectiveStageActions,
                EffectivePolicies = effectiveStagePolicies
            });
        }
    }

    private static IEnumerable<Stage> EnumerateStageLineageRootToLeaf(Stage stage) {
        var stack = new Stack<Stage>();
        var visited = new HashSet<NodeId>();
        for (var current = stage; current is not null; current = current.Parent) {
            if (!visited.Add(current.Id)) {
                break;
            }

            stack.Push(current);
        }
        while (stack.Count > 0) {
            yield return stack.Pop();
        }
    }

    private static IEnumerable<Entity> EnumerateEntityLineageRootToLeaf(Entity entity) {
        var stack = new Stack<Entity>();
        for (var current = entity; current is not null; current = current.ParentEntity) {
            stack.Push(current);
        }
        while (stack.Count > 0) {
            yield return stack.Pop();
        }
    }

    private static IReadOnlyCollection<TNode> MergeByName<TNode>(IEnumerable<TNode> nodes, Func<TNode, string> nameSelector) {
        var byName = new Dictionary<string, TNode>(StringComparer.Ordinal);
        foreach (var node in nodes) {
            byName[nameSelector(node)] = node;
        }
        return byName.Values.ToArray();
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
            ValidateTypeUsage(context, entity, property, property.Type, $"Property '{property.Name}'");
        }

        foreach (var action in entity.Actions) {
            foreach (var parameter in action.Parameters.OfType<Property>()) {
                ValidateTypeUsage(context, entity, parameter, parameter.Type, $"Action '{action.Name}' parameter '{parameter.Name}'");
            }
        }
    }

    private static void ValidateTypeUsage(AnalysisContext context, Entity ownerEntity, Node reportNode, DomainType type, string usage) {
        var expectedDomain = ownerEntity.Domain;
        if (!ReferenceEquals(type.Domain, expectedDomain)) {
            context.ReportError(
                reportNode,
                $"{usage} uses type '{type.Name}' from a different domain.",
                DomainModelDiagnosticCodes.SemanticTypeCompatibility);
            return;
        }

        // Domain modeling no longer treats union wrappers as first-class types; tagged alternatives are modeled via discriminator fields and policies.
    }

    private static void ValidateDiscriminatorConstraints(AnalysisContext context, Entity entity) {
        var discriminatorConstraints = entity.Constraints.OfType<DiscriminatorConstraint>().ToArray();
        if (discriminatorConstraints.Length == 0) {
            return;
        }

        if (discriminatorConstraints.Length > 1) {
            context.ReportError(
                entity,
                $"Entity '{entity.Name}' has multiple discriminator constraints. Only one discriminator constraint is allowed per entity.",
                DomainModelDiagnosticCodes.DiscriminatorExclusivity);
            return;
        }

        var discriminator = discriminatorConstraints[0];

        // Validate discriminator property exists on entity
        var discriminatorProperty = entity.Properties.FirstOrDefault(p => p.Name == discriminator.DiscriminatorPropertyName);
        if (discriminatorProperty is null) {
            context.ReportError(
                entity,
                $"Entity '{entity.Name}' discriminator property '{discriminator.DiscriminatorPropertyName}' was not found on the entity.",
                DomainModelDiagnosticCodes.DiscriminatorExhaustiveness);
            return;
        }

        // Validate discriminator property has an enum constraint (closed tag set)
        var enumConstraint = discriminatorProperty.EffectiveConstraints.OfType<EnumConstraint>().LastOrDefault();
        if (enumConstraint is null) {
            context.ReportError(
                entity,
                $"Entity '{entity.Name}' discriminator property '{discriminator.DiscriminatorPropertyName}' must have an EnumConstraint to define the closed tag set.",
                DomainModelDiagnosticCodes.DiscriminatorExhaustiveness);
            return;
        }

        var enumValues = enumConstraint.Members.Select(static m => m.Name).ToHashSet(StringComparer.Ordinal);
        var variantValues = discriminator.Variants.Select(static v => v.Value).ToHashSet(StringComparer.Ordinal);

        // Check for unknown discriminator values in variants (not in enum)
        var unknownValues = variantValues.Except(enumValues, StringComparer.Ordinal).ToArray();
        if (unknownValues.Length > 0) {
            context.ReportError(
                entity,
                $"Entity '{entity.Name}' discriminator constraint references unknown values not in the enum constraint: {string.Join(", ", unknownValues)}.",
                DomainModelDiagnosticCodes.DiscriminatorExhaustiveness);
        }

        // Check for missing variant coverage (enum values without variant)
        var missingValues = enumValues.Except(variantValues, StringComparer.Ordinal).ToArray();
        if (missingValues.Length > 0) {
            context.ReportWarning(
                entity,
                $"Entity '{entity.Name}' discriminator constraint is missing variant definitions for enum values: {string.Join(", ", missingValues)}.",
                DomainModelDiagnosticCodes.DiscriminatorExhaustiveness);
        }

        // Validate property references in variants
        var propertyNames = entity.Properties.Select(static p => p.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var variant in discriminator.Variants) {
            var allProps = (variant.RequiredProperties ?? [])
                .Concat(variant.ForbiddenProperties ?? [])
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            var invalidProps = allProps.Where(p => !propertyNames.Contains(p)).ToArray();
            if (invalidProps.Length > 0) {
                context.ReportError(
                    entity,
                    $"Entity '{entity.Name}' discriminator variant '{variant.Value}' references non-existent properties: {string.Join(", ", invalidProps)}.",
                    DomainModelDiagnosticCodes.DiscriminatorLeakage);
            }
        }

        // Check for overlapping/duplicate variant values (should be caught in constructor, but validate)
        var duplicateVariants = discriminator.Variants
            .GroupBy(static v => v.Value, StringComparer.Ordinal)
            .Where(static g => g.Count() > 1)
            .Select(static g => g.Key)
            .ToArray();
        if (duplicateVariants.Length > 0) {
            context.ReportError(
                entity,
                $"Entity '{entity.Name}' discriminator constraint has duplicate variant definitions for values: {string.Join(", ", duplicateVariants)}.",
                DomainModelDiagnosticCodes.DiscriminatorExclusivity);
        }
    }
}