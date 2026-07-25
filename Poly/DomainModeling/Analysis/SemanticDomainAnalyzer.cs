using Poly.DomainModeling;
using Poly.DomainModeling.Effects;
using Poly.Introspection;
using Poly.Syntax.Analysis;

namespace Poly.DomainModeling.Analysis;

internal sealed class SemanticDomainAnalyzer : INodeAnalyzer {
    public const string Id = "DomainSemanticDomainAnalyzer";
    public string PassName => Id;
    public string[] Dependencies => [];
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
        ValidateStages(context, entity);
        PublishEffectivePolicies(context, entity);
        PublishEffectiveMemberMetadata(context, entity);
    }

    private static void ValidateStages(AnalysisContext context, Entity entity) {
        var stages = entity.Stages
            .GroupBy(static stage => stage.Name, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Last(), StringComparer.Ordinal);
        // All stages are flat — no parent/child hierarchy in the current DSL surface.
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

        effectivePolicies.AddRange(stage.Policies);
    }

    private static void PublishEffectiveMemberMetadata(AnalysisContext context, Entity entity) {
        // Without entity inheritance, effective members are just the entity's own members.
        context.SetMetadata(entity, new EffectiveMemberMetadata(
            entity.Properties,
            entity.Actions,
            entity.Policies,
            entity.Stages));
    }

    private static void ValidateRelationship(AnalysisContext context, Relationship relationship) {
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