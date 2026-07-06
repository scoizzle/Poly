using Poly.DomainModeling;
using Poly.DomainModeling.Effects;
using Poly.Introspection;
using Poly.Syntax.Analysis;

namespace Poly.DomainModeling.Analysis;

internal sealed class SemanticDomainAnalyzer : INodeAnalyzer {
    public static string PassId => "DomainSemanticDomainAnalyzer";
    public void Analyze(AnalysisContext context, Node node) {
        if (!context.ShouldAnalyze(node)) {
            return;
        }

        switch (node) {
            case Domain domain:
                AnalyzeDomain(context, domain);
                break;
            case PrimitiveType primitiveType:
                AnalyzePrimitiveType(context, primitiveType);
                break;
            case Property property:
                ResolveTypeReference(context, property.Type, property, $"Property '{property.Name}'");
                break;
            case InvocationResult.Member resultMember:
                ResolveTypeReference(context, resultMember.Type, resultMember, $"Result member '{resultMember.Name}'");
                break;
            case Relationship relationship:
                ValidateRelationship(context, relationship);
                break;
            case CreateEntityInstance createEntityInstance:
                ValidateCreateEntity(context, createEntityInstance);
                break;
            case PublishEventEffect:
            case StageTransitionEffect:
                // Effect validation (bindings, availability) is future work.
                break;
            case Entity entity:
                ValidateEntity(context, entity);
                break;
        }

        this.AnalyzeChildren(context, node);
    }

    private static void AnalyzeDomain(AnalysisContext context, Domain domain) {
        if (!context.TryBeginAnalyzerVisit<SemanticDomainAnalyzer>(domain)) {
            return;
        }

        var types = domain.Types
            .GroupBy(static type => type.Name, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Last(), StringComparer.Ordinal);

        DomainTypeLookupMetadata lookup = new(
            domain,
            types,
            new HashSet<Entity>(domain.Types.OfType<Entity>(), ReferenceEqualityComparer.Instance));

        context.SetMetadata(default, lookup);
    }

    private static void AnalyzePrimitiveType(AnalysisContext context, PrimitiveType primitiveType) {
        if (!context.TryBeginAnalyzerVisit<SemanticDomainAnalyzer>(primitiveType)) {
            return;
        }

        if (primitiveType.TypeCategory.Is(TypeCategory.Nullable)) {
            context.ReportError(
                primitiveType,
                $"Primitive '{primitiveType.Name}' must not use TypeCategory.Nullable. Domain nullability is modeled by RequiredConstraint.",
                DomainModelDiagnosticCodes.SemanticTypeCompatibility);
        }

        if (primitiveType.TypeCategory.Is(TypeCategory.Collection) || primitiveType.TypeCategory.Is(TypeCategory.Keyed)) {
            context.ReportError(
                primitiveType,
                $"Primitive '{primitiveType.Name}' must not use collection categories. Domain multiplicity is modeled through relationships.",
                DomainModelDiagnosticCodes.SemanticTypeCompatibility);
        }
    }

    private static void ResolveTypeReference(
        AnalysisContext context,
        DomainTypeReference typeReference,
        Node reportNode,
        string usage) {
        ArgumentNullException.ThrowIfNull(typeReference);

        if (context.GetMetadata<DomainTypeLookupMetadata>(default) is not DomainTypeLookupMetadata lookup) {
            return;
        }

        if (!lookup.Types.TryGetValue(typeReference.TypeName, out var type)) {
            context.ReportStructuralFailure(
                reportNode,
                $"{usage} references unknown type '{typeReference.TypeName}'.",
                DomainModelDiagnosticCodes.SemanticReferenceResolution);
            return;
        }

        context.SetMetadata(typeReference, new ResolvedTypeReferenceMetadata(type));
    }

    private static void ValidateEntity(AnalysisContext context, Entity entity) {
        if (!context.TryBeginAnalyzerVisit<SemanticDomainAnalyzer>(entity)) {
            return;
        }

        ResolveEntityEvents(context, entity);
        ValidateEntityParentCycle(context, entity);
        ValidateStages(context, entity);
        PublishEffectivePolicies(context, entity);
        PublishEffectiveMemberMetadata(context, entity);
    }

    private static void ResolveEntityEvents(AnalysisContext context, Entity entity) {
        foreach (var @event in entity.Events) {
            ResolveTypeReference(context, @event, entity, $"Entity '{entity.Name}' event");

            if (context.GetMetadata<ResolvedTypeReferenceMetadata>(@event) is not ResolvedTypeReferenceMetadata resolved) {
                continue;
            }

            if (resolved.Type is Event) {
                continue;
            }

            context.ReportError(
                entity,
                $"Entity '{entity.Name}' event '{@event.TypeName}' must resolve to an event.",
                DomainModelDiagnosticCodes.SemanticTypeCompatibility);
        }
    }

    private static void ValidateEntityParentCycle(AnalysisContext context, Entity entity) {
        if (entity.ParentEntityName is null) return;

        var lookup = context.GetMetadata<DomainTypeLookupMetadata>(default);
        if (lookup is null) return;

        var visited = new HashSet<string>(StringComparer.Ordinal) { entity.Name };
        var currentName = entity.ParentEntityName;

        while (currentName is not null) {
            if (!visited.Add(currentName)) {
                context.ReportStructuralFailure(
                    entity,
                    $"Entity '{entity.Name}' participates in an inheritance cycle.",
                    DomainModelDiagnosticCodes.StructuralCycle);
                return;
            }

            if (!lookup.Types.TryGetValue(currentName, out var parentType) || parentType is not Entity parentEntity) {
                context.ReportStructuralFailure(
                    entity,
                    $"Entity '{entity.Name}' references unknown parent entity '{currentName}'.",
                    DomainModelDiagnosticCodes.SemanticReferenceResolution);
                return;
            }

            currentName = parentEntity.ParentEntityName;
        }
    }

    private static void ValidateStages(AnalysisContext context, Entity entity) {
        var stages = entity.Stages
            .GroupBy(static stage => stage.Name, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Last(), StringComparer.Ordinal);

        foreach (var stage in entity.Stages) {
            if (stage.Parent is null) {
                continue;
            }

            if (!stages.ContainsKey(stage.Parent.StageName)) {
                context.ReportStructuralFailure(
                    stage,
                    $"Stage '{stage.Name}' on entity '{entity.Name}' references unknown parent stage '{stage.Parent.StageName}'.",
                    DomainModelDiagnosticCodes.SemanticReferenceResolution);
            }
        }

        foreach (var stage in entity.Stages) {
            if (HasStageCycle(stage, stages)) {
                context.ReportStructuralFailure(
                    stage,
                    $"Stage '{stage.Name}' on entity '{entity.Name}' has a parent cycle.",
                    DomainModelDiagnosticCodes.SemanticTypeCompatibility);
            }
        }

        foreach (var stage in entity.Stages) {
            if (stage.Parent is not null && stages.TryGetValue(stage.Parent.StageName, out var resolvedParent)) {
                context.SetMetadata(stage, new ResolvedStageParentMetadata(resolvedParent));
            }
        }

        PublishStageLineageMetadata(context, entity, stages);
    }

    private static bool HasStageCycle(Stage stage, IReadOnlyDictionary<string, Stage> stages) {
        HashSet<string> visited = [stage.Name];
        var current = stage;

        while (current.Parent is not null) {
            if (!stages.TryGetValue(current.Parent.StageName, out current!)) {
                return false;
            }

            if (!visited.Add(current.Name)) {
                return true;
            }
        }

        return false;
    }

    private static void PublishEffectivePolicies(AnalysisContext context, Entity entity) {
        var entityPolicies = entity.Policies.ToArray();
        if (entityPolicies.Length > 0) {
            context.SetMetadata(entity, new EffectivePoliciesMetadata(entityPolicies));
        }

        foreach (var action in entity.Actions) {
            var actionPolicies = entityPolicies.Concat(action.Policies).ToArray();
            if (actionPolicies.Length > 0) {
                context.SetMetadata(action, new EffectivePoliciesMetadata(actionPolicies));
            }
        }

        var stages = entity.Stages
            .GroupBy(static stage => stage.Name, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Last(), StringComparer.Ordinal);

        foreach (var stage in entity.Stages) {
            var stagePolicies = GetEffectiveStagePolicies(entityPolicies, stage, stages);
            if (stagePolicies.Count > 0) {
                context.SetMetadata(stage, new EffectivePoliciesMetadata(stagePolicies));
            }

            foreach (var action in stage.Actions) {
                var actionPolicies = stagePolicies.Concat(action.Policies).ToArray();
                if (actionPolicies.Length > 0) {
                    context.SetMetadata(action, new EffectivePoliciesMetadata(actionPolicies));
                }
            }
        }
    }

    private static IReadOnlyList<Policy> GetEffectiveStagePolicies(
        IReadOnlyList<Policy> entityPolicies,
        Stage stage,
        IReadOnlyDictionary<string, Stage> stages) {
        List<Policy> effectivePolicies = [.. entityPolicies];
        AppendStagePolicies(stage, stages, effectivePolicies, []);
        return effectivePolicies;
    }

    private static void AppendStagePolicies(
        Stage stage,
        IReadOnlyDictionary<string, Stage> stages,
        List<Policy> effectivePolicies,
        HashSet<string> visited) {
        if (!visited.Add(stage.Name)) {
            return;
        }

        if (stage.Parent is not null && stages.TryGetValue(stage.Parent.StageName, out var parent)) {
            AppendStagePolicies(parent, stages, effectivePolicies, visited);
        }

        effectivePolicies.AddRange(stage.Policies);
    }

    private static void PublishStageLineageMetadata(
        AnalysisContext context,
        Entity entity,
        IReadOnlyDictionary<string, Stage> stages) {
        foreach (var stage in entity.Stages) {
            var ancestors = CollectStageAncestors(stage, stages);
            if (ancestors.Count > 0) {
                context.SetMetadata(stage, new StageLineageMetadata(ancestors.Count, ancestors));
            }
        }
    }

    private static List<Stage> CollectStageAncestors(
        Stage stage,
        IReadOnlyDictionary<string, Stage> stages) {
        var ancestors = new List<Stage>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var current = stage;

        while (current.Parent is not null) {
            if (!visited.Add(current.Parent.StageName)) break;
            if (stages.TryGetValue(current.Parent.StageName, out var parent)) {
                ancestors.Add(parent);
                current = parent;
            }
            else {
                break;
            }
        }

        return ancestors;
    }

    private static void PublishEffectiveMemberMetadata(AnalysisContext context, Entity entity) {
        var lookup = context.GetMetadata<DomainTypeLookupMetadata>(default);
        if (lookup is null) return;

        var lineage = EnumerateEntityLineageRootToLeaf(entity, lookup).ToArray();

        var effectiveProperties = MergeByName(
            lineage.SelectMany(static e => e.Properties),
            static p => p.Name);
        var effectiveActions = MergeByName(
            lineage.SelectMany(static e => e.Actions),
            static a => a.Name);
        var effectivePolicies = MergeByName(
            lineage.SelectMany(static e => e.Policies),
            static p => p.Name);
        var effectiveEvents = MergeByName(
            lineage.SelectMany(e => e.Events),
            static e => e.TypeName); // DomainTypeReference uses TypeName
        var effectiveStages = MergeByName(
            lineage.SelectMany(static e => e.Stages),
            static s => s.Name);

        context.SetMetadata(entity, new EffectiveMemberMetadata(
            effectiveProperties,
            effectiveActions,
            effectivePolicies,
            effectiveEvents,
            effectiveStages));
    }

    private static IEnumerable<Entity> EnumerateEntityLineageRootToLeaf(
        Entity entity,
        DomainTypeLookupMetadata lookup) {
        if (entity.ParentEntityName is null) {
            yield return entity;
            yield break;
        }

        var chain = new List<Entity>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        Entity? current = entity;

        while (current is not null) {
            if (!visited.Add(current.Name)) break;
            chain.Add(current);
            if (current.ParentEntityName is not null
                && lookup.Types.TryGetValue(current.ParentEntityName, out var parentType)
                && parentType is Entity parent) {
                current = parent;
            }
            else {
                current = null;
            }
        }

        chain.Reverse();

        foreach (var e in chain) {
            yield return e;
        }
    }

    private static List<T> MergeByName<T>(
        IEnumerable<T> items,
        Func<T, string> nameSelector) where T : class {
        var merged = new Dictionary<string, T>(StringComparer.Ordinal);

        foreach (var item in items) {
            merged[nameSelector(item)] = item;
        }

        return merged.Values.ToList();
    }

    private static void ValidateRelationship(AnalysisContext context, Relationship relationship) {
        if (!context.TryBeginAnalyzerVisit<SemanticDomainAnalyzer>(relationship)) {
            return;
        }

        ValidateRelationshipEndpoint(context, relationship.Source, relationship, relationship.Name, "source");
        ValidateRelationshipEndpoint(context, relationship.Target, relationship, relationship.Name, "target");
    }

    private static void ValidateRelationshipEndpoint(
        AnalysisContext context,
        DomainTypeReference endpoint,
        Relationship relationship,
        string relationshipName,
        string endpointKind) {
        ArgumentNullException.ThrowIfNull(endpoint);

        ResolveTypeReference(context, endpoint, relationship, $"Relationship '{relationshipName}' {endpointKind}");

        if (context.GetMetadata<ResolvedTypeReferenceMetadata>(endpoint) is not ResolvedTypeReferenceMetadata resolved) {
            return;
        }

        if (resolved.Type is Entity) {
            return;
        }

        context.ReportError(
            relationship,
            $"Relationship '{relationshipName}' {endpointKind} '{endpoint.TypeName}' must resolve to an entity.",
            DomainModelDiagnosticCodes.SemanticTypeCompatibility);
    }

    private static void ValidateCreateEntity(AnalysisContext context, CreateEntityInstance createEntityInstance) {
        if (!context.TryBeginAnalyzerVisit<SemanticDomainAnalyzer>(createEntityInstance)) {
            return;
        }

        ResolveTypeReference(context, createEntityInstance.Type, createEntityInstance, "CreateEntityInstance");

        if (context.GetMetadata<ResolvedTypeReferenceMetadata>(createEntityInstance.Type) is not ResolvedTypeReferenceMetadata resolved) {
            return;
        }

        if (resolved.Type is Entity or ValueType) {
            return;
        }

        context.ReportError(
            createEntityInstance,
            $"CreateEntityInstance type '{createEntityInstance.Type.TypeName}' must resolve to an Entity or ValueType.",
            DomainModelDiagnosticCodes.SemanticTypeCompatibility);
    }
}