using Poly.Analysis;
using Poly.DomainModeling.Effects;
using Poly.Introspection;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// First metadata pass: one name→member catalog plus type-reference resolution.
/// Downstream passes read <see cref="DomainCatalogMetadata"/> (and the same
/// type/relationship maps aliased on <c>default</c>). They do not rebuild indexes.
/// </summary>
internal sealed class DomainCatalogPass : INodeAnalyzer {
    public const string Id = "DomainCatalogPass";
    public string PassName => Id;
    public string[] Dependencies => [];

    public void Analyze(AnalysisContext context, Node node) {
        if (!context.ShouldAnalyze(node))
            return;

        switch (node) {
            case Domain domain:
                PublishCatalog(context, domain);
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
            case Entity entity:
                PublishOwnerIndex(context, entity);
                break;
        }

        this.AnalyzeChildren(context, node);
    }

    private static void PublishCatalog(AnalysisContext context, Domain domain) {
        var types = domain.Types
            .GroupBy(static type => type.Name, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Last(), StringComparer.Ordinal);
        foreach (var contract in domain.ImportedContracts) {
            foreach (var vt in contract.Types)
                types[vt.Name] = vt;
        }

        var typeLookup = new DomainTypeLookupMetadata(
            domain,
            types,
            new HashSet<Entity>(domain.Types.OfType<Entity>(), ReferenceEqualityComparer.Instance));
        var relationships = new RelationshipLookupMetadata(BuildRelationshipLookup(context, domain));

        var actionsByEntity = new Dictionary<string, ActionResolutionMetadata>(StringComparer.Ordinal);
        foreach (var entity in typeLookup.Entities)
            actionsByEntity[entity.Name] = BuildActionResolution(entity);

        var index = BuildMutationTargetIndex(domain, typeLookup, relationships);

        context.SetMetadata(default, typeLookup);
        context.SetMetadata(default, relationships);
        context.SetMetadata(domain, new DomainCatalogMetadata(
            Domain: domain,
            Types: typeLookup,
            Relationships: relationships,
            Index: index,
            ActionsByEntityName: actionsByEntity));
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, Relationship>> BuildRelationshipLookup(
        AnalysisContext context, Domain domain) {
        var bySource = new Dictionary<string, IReadOnlyDictionary<string, Relationship>>(StringComparer.Ordinal);
        foreach (var entity in domain.Types.OfType<Entity>()) {
            var byNav = new Dictionary<string, Relationship>(StringComparer.Ordinal);
            foreach (var rel in entity.Navigations) {
                if (!byNav.TryAdd(rel.Name, rel)) {
                    context.ReportError(
                        rel,
                        $"Relationship '{rel.Name}' is declared more than once on source entity " +
                        $"'{entity.Name}'. Relationship names must be unique within their source entity.",
                        DomainModelDiagnosticCodes.SemanticReferenceResolution);
                }
            }
            bySource[entity.Name] = byNav;
        }
        return bySource;
    }

    private static ActionResolutionMetadata BuildActionResolution(Entity entity) {
        var entityActions = entity.Actions
            .GroupBy(a => a.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);

        var stageActions = new Dictionary<string, IReadOnlyDictionary<string, Action>>(StringComparer.Ordinal);
        foreach (var stage in entity.Stages) {
            stageActions[stage.Name] = stage.Actions
                .GroupBy(a => a.Name, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);
        }

        return new ActionResolutionMetadata(entityActions, stageActions);
    }

    private static MutationTargetIndexMetadata BuildMutationTargetIndex(
        Domain domain,
        DomainTypeLookupMetadata types,
        RelationshipLookupMetadata relationships) {
        var entitiesByName = types.Entities
            .GroupBy(e => e.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);

        var stagesByEntity = new Dictionary<string, IReadOnlyDictionary<string, Stage>>(StringComparer.Ordinal);
        var actionsByEntity = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<Action>>>(StringComparer.Ordinal);
        var entityPoliciesByEntity = new Dictionary<string, IReadOnlyDictionary<string, Policy>>(StringComparer.Ordinal);
        var stagePoliciesByEntity = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, Policy>>>(StringComparer.Ordinal);
        var actionPoliciesByEntity = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, Policy>>>(StringComparer.Ordinal);

        foreach (var entity in entitiesByName.Values) {
            stagesByEntity[entity.Name] = entity.Stages
                .GroupBy(s => s.Name, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);

            var actionLookup = new Dictionary<string, IReadOnlyList<Action>>(StringComparer.Ordinal);
            foreach (var action in entity.Actions.GroupBy(a => a.Name, StringComparer.Ordinal))
                actionLookup[action.Key] = action.ToList();
            foreach (var stage in entity.Stages) {
                foreach (var action in stage.Actions.GroupBy(a => a.Name, StringComparer.Ordinal)) {
                    if (!actionLookup.TryGetValue(action.Key, out var existing)) {
                        actionLookup[action.Key] = action.ToList();
                        continue;
                    }
                    actionLookup[action.Key] = [.. existing, .. action];
                }
            }
            actionsByEntity[entity.Name] = actionLookup;

            entityPoliciesByEntity[entity.Name] = entity.Policies
                .GroupBy(p => p.Name, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);

            var stagePolicyLookup = new Dictionary<string, IReadOnlyDictionary<string, Policy>>(StringComparer.Ordinal);
            var actionPolicyLookup = new Dictionary<string, IReadOnlyDictionary<string, Policy>>(StringComparer.Ordinal);
            foreach (var stage in entity.Stages) {
                stagePolicyLookup[stage.Name] = stage.Policies
                    .GroupBy(p => p.Name, StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);
                foreach (var action in stage.Actions) {
                    actionPolicyLookup[action.Name] = action.Policies
                        .GroupBy(p => p.Name, StringComparer.Ordinal)
                        .ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);
                }
            }
            foreach (var action in entity.Actions) {
                actionPolicyLookup[action.Name] = action.Policies
                    .GroupBy(p => p.Name, StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);
            }
            stagePoliciesByEntity[entity.Name] = stagePolicyLookup;
            actionPoliciesByEntity[entity.Name] = actionPolicyLookup;
        }

        return new MutationTargetIndexMetadata(
            TypesByName: types.Types,
            EntitiesByName: entitiesByName,
            RelationshipsByName: relationships.BySourceEntity,
            StagesByEntity: stagesByEntity,
            ActionsByEntity: actionsByEntity,
            EntityPoliciesByEntity: entityPoliciesByEntity,
            StagePoliciesByEntity: stagePoliciesByEntity,
            ActionPoliciesByEntity: actionPoliciesByEntity);
    }

    private static void PublishOwnerIndex(AnalysisContext context, Entity entity) {
        foreach (var action in entity.Actions)
            context.SetMetadata(action, new OwnerEntityMetadata(entity));
        foreach (var stage in entity.Stages) {
            context.SetMetadata(stage, new OwnerEntityMetadata(entity));
            foreach (var action in stage.Actions)
                context.SetMetadata(action, new OwnerEntityMetadata(entity));
        }
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

        var lookup = context.GetTypeLookup();
        if (lookup is null)
            return;

        if (!lookup.Types.TryGetValue(typeReference.TypeName, out var type)) {
            context.ReportStructuralFailure(
                reportNode,
                $"{usage} references unknown type '{typeReference.TypeName}'.",
                DomainModelDiagnosticCodes.SemanticReferenceResolution);
            return;
        }

        context.SetMetadata(typeReference, new ResolvedTypeReferenceMetadata(type));
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

        if (context.GetMetadata<ResolvedTypeReferenceMetadata>(endpoint) is not ResolvedTypeReferenceMetadata resolved)
            return;

        if (resolved.Type is Entity)
            return;

        context.ReportError(
            relationship,
            $"Relationship '{relationshipName}' {endpointKind} '{endpoint.TypeName}' must resolve to an entity.",
            DomainModelDiagnosticCodes.SemanticTypeCompatibility);
    }

    private static void ValidateCreateEntity(AnalysisContext context, CreateEntityInstance createEntityInstance) {
        ResolveTypeReference(context, createEntityInstance.Type, createEntityInstance, "CreateEntityInstance");

        if (context.GetMetadata<ResolvedTypeReferenceMetadata>(createEntityInstance.Type) is not ResolvedTypeReferenceMetadata resolved)
            return;

        if (resolved.Type is Entity or ValueType)
            return;

        context.ReportError(
            createEntityInstance,
            $"CreateEntityInstance type '{createEntityInstance.Type.TypeName}' must resolve to an Entity or ValueType.",
            DomainModelDiagnosticCodes.SemanticTypeCompatibility);
    }
}