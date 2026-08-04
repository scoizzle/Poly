using Poly.Analysis;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Shared semantic lookup helpers over analysis metadata.
/// Product paths with a domain key read <see cref="DomainCatalogMetadata"/> only
/// (dual-write of ARM/MTI retired). Domain-less helpers fall back to
/// intermediate Semantic DTLM/RLM bags used mid-pipeline.
/// Methods fail closed (false/empty) when required metadata is absent —
/// they do not tree-scan the domain.
/// <para>
/// <b>SA fallthrough (stage-action → entity-action)</b> is implemented only in
/// <see cref="TryResolveAction"/>. Empty stage-copy (no effects/policies) yields
/// the entity action. Parameters are intentionally ignored because
/// <c>AddActionToStageChange</c> copies them onto the stage shell.
/// </para>
/// </summary>
public static class DomainSemanticLookupExtensions {

    internal static DomainCatalogMetadata? GetCatalog(this INodeMetadataProvider analysis, Domain domain) =>
        analysis.GetMetadata<DomainCatalogMetadata>(domain);

    /// <summary>
    /// Catalog action map for <paramref name="entity"/>. No entity-keyed ARM dual-write.
    /// </summary>
    internal static ActionResolutionMetadata? GetActionResolution(
        this INodeMetadataProvider analysis,
        Domain? domain,
        Entity entity) {
        if (domain is not null) {
            var catalog = analysis.GetCatalog(domain);
            if (catalog is not null
                && catalog.ActionsByEntityName.TryGetValue(entity.Name, out var fromCatalog))
                return fromCatalog;
            return null;
        }

        var dtlm = analysis.GetMetadata<DomainTypeLookupMetadata>(default);
        if (dtlm is not null) {
            var catalog = analysis.GetCatalog(dtlm.Domain);
            if (catalog is not null
                && catalog.ActionsByEntityName.TryGetValue(entity.Name, out var fromCatalog))
                return fromCatalog;
        }

        return null;
    }

    /// <summary>
    /// Catalog mutation index only (no separate domain-keyed MTI).
    /// </summary>
    internal static MutationTargetIndexMetadata? GetMutationIndex(
        this INodeMetadataProvider analysis,
        Domain domain) =>
        analysis.GetCatalog(domain)?.Index;

    /// <summary>
    /// Catalog type lookup when <paramref name="domain"/> is set; else intermediate DTLM.
    /// </summary>
    internal static DomainTypeLookupMetadata? GetTypeLookup(
        this INodeMetadataProvider analysis,
        Domain? domain = null) {
        if (domain is not null) {
            var fromCatalog = analysis.GetCatalog(domain)?.Types;
            if (fromCatalog is not null) return fromCatalog;
            return null;
        }
        return analysis.GetMetadata<DomainTypeLookupMetadata>(default);
    }

    /// <summary>
    /// Catalog relationships when <paramref name="domain"/> is set; else intermediate RLM.
    /// </summary>
    internal static RelationshipLookupMetadata? GetRelationshipLookup(
        this INodeMetadataProvider analysis,
        Domain? domain = null) {
        if (domain is not null) {
            var fromCatalog = analysis.GetCatalog(domain)?.Relationships;
            if (fromCatalog is not null) return fromCatalog;
            return null;
        }
        return analysis.GetMetadata<RelationshipLookupMetadata>(default);
    }

    // ── Stage resolution ──────────────────────────────────────

    public static bool TryGetStage(this INodeMetadataProvider analysis, Entity entity, string stageName, out Stage? stage) {
        var esm = analysis.GetMetadata<EntityStructureMetadata>(entity);
        if (esm?.StageByName is not null && esm.StageByName.TryGetValue(stageName, out stage))
            return true;
        stage = null;
        return false;
    }

    // ── Action resolution ─────────────────────────────────────

    /// <summary>
    /// Resolves an action by name for <paramref name="entity"/>.
    /// Stage-scoped actions win when <paramref name="currentStage"/> is set;
    /// SA fallthrough (empty stage-copy → entity action) is applied here only.
    /// </summary>
    public static bool TryResolveAction(
        this AnalysisResult analysis,
        Entity entity,
        string? currentStage,
        string actionName,
        out Action? action) =>
        analysis.TryResolveAction(domain: null, entity, currentStage, actionName, out action);

    /// <summary>
    /// Domain-keyed overload: catalog ARM for <paramref name="domain"/> (or domain from DTLM).
    /// </summary>
    public static bool TryResolveAction(
        this AnalysisResult analysis,
        Domain? domain,
        Entity entity,
        string? currentStage,
        string actionName,
        out Action? action) {
        var arm = analysis.GetActionResolution(domain, entity);
        if (arm is null) {
            action = null;
            return false;
        }

        if (currentStage is not null
            && arm.StageActions.TryGetValue(currentStage, out var stageActions)
            && stageActions.TryGetValue(actionName, out var stageAction)) {
            // SA: empty stage-copy (no effects/policies) → entity action.
            // Parameters intentionally ignored (AddActionToStageChange copies them).
            if (stageAction.Effects.Count == 0
                && stageAction.Policies.Count == 0
                && arm.EntityActions.TryGetValue(actionName, out var entityActionOverride)) {
                action = entityActionOverride;
                return true;
            }
            action = stageAction;
            return true;
        }

        if (arm.EntityActions.TryGetValue(actionName, out var entityAction)) {
            action = entityAction;
            return true;
        }

        action = null;
        return false;
    }

    // ── Effective surface (stage policies / actions) ──────────

    /// <summary>
    /// Effective policies at a stage. Prefer the canonical
    /// <see cref="StageCapabilityMetadata"/> surface; else re-apply
    /// <see cref="DomainEffectiveSurface"/> over catalog Index maps
    /// (entity + stage policies only — not action policies).
    /// </summary>
    public static IReadOnlyList<Policy> GetEffectivePolicies(
        this AnalysisResult analysis,
        Domain domain,
        Entity entity,
        string stageName) {
        // Fail closed on unknown stage (symmetric with GetEffectiveActions).
        if (!analysis.TryGetStage(entity, stageName, out var stage) || stage is null)
            return Array.Empty<Policy>();

        var cap = analysis.GetMetadata<StageCapabilityMetadata>(stage);
        if (cap is not null)
            return cap.View.EffectivePolicies;

        var mti = analysis.GetMutationIndex(domain);
        if (mti is null) return Array.Empty<Policy>();

        mti.EntityPoliciesByEntity.TryGetValue(entity.Name, out var entityPolicies);
        IReadOnlyDictionary<string, Policy>? stageScoped = null;
        if (mti.StagePoliciesByEntity.TryGetValue(entity.Name, out var stagePolicies))
            stagePolicies.TryGetValue(stageName, out stageScoped);
        return DomainEffectiveSurface.ComposeStagePolicies(entityPolicies, stageScoped);
    }

    /// <summary>
    /// Effective actions at a stage. Prefer the canonical
    /// <see cref="StageCapabilityMetadata"/> surface; else stage-local actions
    /// via <see cref="DomainEffectiveSurface"/> (stage-local only).
    /// </summary>
    public static IReadOnlyList<Action> GetEffectiveActions(
        this AnalysisResult analysis,
        Domain domain,
        Entity entity,
        string stageName) {
        if (!analysis.TryGetStage(entity, stageName, out var stage) || stage is null)
            return Array.Empty<Action>();

        // Stage-local only (Capability EffectiveActions mirror stage.Actions; SA entity
        // actions stay on TryResolveAction for runtime dispatch, not this list).
        if (analysis.GetMetadata<StageCapabilityMetadata>(stage) is not null
            || analysis.GetCatalog(domain) is not null)
            return DomainEffectiveSurface.ComposeStageActions(stage);

        return Array.Empty<Action>();
    }

    // ── Relationship resolution ───────────────────────────────

    public static bool TryGetRelationship(this AnalysisResult analysis, string relationshipName, out Relationship? relationship) {
        var rlm = analysis.GetRelationshipLookup();
        if (rlm is not null && rlm.Relationships.TryGetValue(relationshipName, out relationship))
            return true;
        relationship = null;
        return false;
    }

    /// <summary>
    /// Resolves a relationship using catalog when <paramref name="domain"/> is provided.
    /// </summary>
    public static bool TryGetRelationship(
        this AnalysisResult analysis,
        Domain domain,
        string relationshipName,
        out Relationship? relationship) {
        var rlm = analysis.GetRelationshipLookup(domain);
        if (rlm is not null && rlm.Relationships.TryGetValue(relationshipName, out relationship))
            return true;
        relationship = null;
        return false;
    }

    public static IReadOnlyList<Relationship> GetOutboundRelationships(this AnalysisResult analysis, string entityName) {
        var rcm = analysis.GetMetadata<RelationshipContractMetadata>(default);
        if (rcm is not null) {
            return rcm.Contracts
                .Where(c => string.Equals(c.SourceEntityName, entityName, StringComparison.Ordinal))
                .Select(c => {
                    analysis.TryGetRelationship(c.Name, out var rel);
                    return rel;
                })
                .OfType<Relationship>()
                .ToList();
        }
        return Array.Empty<Relationship>();
    }

    public static IReadOnlyList<Relationship> GetInboundRelationships(this AnalysisResult analysis, string entityName) {
        var rcm = analysis.GetMetadata<RelationshipContractMetadata>(default);
        if (rcm is not null) {
            return rcm.Contracts
                .Where(c => string.Equals(c.TargetEntityName, entityName, StringComparison.Ordinal))
                .Select(c => {
                    analysis.TryGetRelationship(c.Name, out var rel);
                    return rel;
                })
                .OfType<Relationship>()
                .ToList();
        }
        return Array.Empty<Relationship>();
    }

    // ── Entity/type resolution ────────────────────────────────

    public static bool TryGetEntity(this AnalysisResult analysis, string typeName, out Entity? entity) {
        var dtlm = analysis.GetTypeLookup();
        if (dtlm is not null
            && dtlm.Types.TryGetValue(typeName, out var domainType)
            && domainType is Entity e) {
            entity = e;
            return true;
        }
        entity = null;
        return false;
    }

    public static bool TryGetEntity(this AnalysisResult analysis, Domain domain, string typeName, out Entity? entity) {
        var dtlm = analysis.GetTypeLookup(domain);
        if (dtlm is not null
            && dtlm.Types.TryGetValue(typeName, out var domainType)
            && domainType is Entity e) {
            entity = e;
            return true;
        }
        entity = null;
        return false;
    }

    public static bool TryGetEnumType(this AnalysisResult analysis, string typeName, out EnumType? enumType) {
        var dtlm = analysis.GetTypeLookup();
        if (dtlm is not null
            && dtlm.Types.TryGetValue(typeName, out var domainType)
            && domainType is EnumType et) {
            enumType = et;
            return true;
        }
        enumType = null;
        return false;
    }

    public static bool TryGetEnumType(this AnalysisResult analysis, Domain domain, string typeName, out EnumType? enumType) {
        var dtlm = analysis.GetTypeLookup(domain);
        if (dtlm is not null
            && dtlm.Types.TryGetValue(typeName, out var domainType)
            && domainType is EnumType et) {
            enumType = et;
            return true;
        }
        enumType = null;
        return false;
    }
}