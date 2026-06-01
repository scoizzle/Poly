using Poly.DomainModeling;
using Poly.DomainModeling.Effects;
using Poly.Introspection;
using Poly.Syntax.Analysis;

namespace Poly.DomainModeling.Analysis;

internal sealed class SemanticDomainAnalyzer : INodeAnalyzer {
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

        ValidateStages(context, entity);
        PublishEffectivePolicies(context, entity);
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
        context.SetMetadata(entity, new EffectivePoliciesMetadata(entityPolicies));

        foreach (var action in entity.Actions) {
            context.SetMetadata(action, new EffectivePoliciesMetadata([.. entityPolicies, .. action.Policies]));
        }

        var stages = entity.Stages
            .GroupBy(static stage => stage.Name, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Last(), StringComparer.Ordinal);

        foreach (var stage in entity.Stages) {
            var stagePolicies = GetEffectiveStagePolicies(entityPolicies, stage, stages);
            context.SetMetadata(stage, new EffectivePoliciesMetadata(stagePolicies));

            foreach (var action in stage.Actions) {
                context.SetMetadata(action, new EffectivePoliciesMetadata([.. stagePolicies, .. action.Policies]));
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