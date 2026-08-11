using Poly.Analysis;
using Poly.DomainModeling;
using Poly.DomainModeling.Effects;
using Poly.Introspection;

namespace Poly.DomainModeling.Analysis;

internal sealed class SemanticDomainAnalyzer : INodeAnalyzer {
    public const string Id = "DomainSemanticDomainAnalyzer";
    public string PassName => Id;
    // Root fact publisher (DTLM/RLM/EPM/…); no upstream analysis bags.
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

        var relationships = BuildRelationshipLookup(context, domain);

        context.SetMetadata(default, lookup);
        context.SetMetadata(default, new RelationshipLookupMetadata(relationships));
    }

    /// <summary>
    /// Builds the source-scoped relationship index: (source entity name → nav name → relationship).
    /// Relationship identity is (source entity, name); a relationship is an entity-owned
    /// navigation. Same-source duplicates are a model error and fail closed.
    /// </summary>
    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, Relationship>> BuildRelationshipLookup(
        AnalysisContext context, Domain domain) {
        var bySource = new Dictionary<string, IReadOnlyDictionary<string, Relationship>>(StringComparer.Ordinal);
        foreach (var entity in domain.Types.OfType<Entity>()) {
            var byNav = new Dictionary<string, Relationship>(StringComparer.Ordinal);
            foreach (var rel in entity.Navigations) {
                if (byNav.ContainsKey(rel.Name)) {
                    context.ReportError(
                        rel,
                        $"Relationship '{rel.Name}' is declared more than once on source entity " +
                        $"'{entity.Name}'. Relationship names must be unique within their source entity.",
                        DomainModelDiagnosticCodes.SemanticReferenceResolution);
                    continue;
                }
                byNav[rel.Name] = rel;
            }
            bySource[entity.Name] = byNav;
        }
        return bySource;
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
        PublishOwnerIndex(context, entity);
    }

    private static void ValidateStages(AnalysisContext context, Entity entity) {
        var stages = entity.Stages
            .GroupBy(static stage => stage.Name, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Last(), StringComparer.Ordinal);
        // All stages are flat — no parent/child hierarchy in the current DSL surface.
    }

    /// <summary>
    /// Publishes <see cref="OwnerEntityMetadata"/> on every action and stage node so
    /// downstream passes resolve "which entity owns this action/stage" in O(1) instead
    /// of scanning every entity's members.
    /// </summary>
    private static void PublishOwnerIndex(AnalysisContext context, Entity entity) {
        foreach (var action in entity.Actions)
            context.SetMetadata(action, new OwnerEntityMetadata(entity));
        foreach (var stage in entity.Stages) {
            context.SetMetadata(stage, new OwnerEntityMetadata(entity));
            foreach (var action in stage.Actions)
                context.SetMetadata(action, new OwnerEntityMetadata(entity));
        }
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