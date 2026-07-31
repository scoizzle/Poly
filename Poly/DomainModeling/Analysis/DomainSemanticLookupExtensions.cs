using Poly.Analysis;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Shared semantic lookup helpers on analysis metadata.
/// Prefer <see cref="DomainCatalogMetadata"/> when present (DAS W1).
/// Methods fail closed (false/empty) when required metadata is absent —
/// they do not tree-scan the domain.
/// </summary>
public static class DomainSemanticLookupExtensions {

    internal static DomainCatalogMetadata? GetCatalog(this INodeMetadataProvider analysis, Domain domain) =>
        analysis.GetMetadata<DomainCatalogMetadata>(domain);

    // ── Stage resolution ──────────────────────────────────────

    public static bool TryGetStage(this INodeMetadataProvider analysis, Entity entity, string stageName, out Stage? stage) {
        var esm = analysis.GetMetadata<EntityStructureMetadata>(entity);
        if (esm?.StageByName is not null && esm.StageByName.TryGetValue(stageName, out stage))
            return true;
        stage = null;
        return false;
    }

    // ── Action resolution ─────────────────────────────────────

    public static bool TryResolveAction(
        this AnalysisResult analysis,
        Entity entity,
        string? currentStage,
        string actionName,
        out Action? action) {
        ActionResolutionMetadata? arm = null;
        var dtlm = analysis.GetMetadata<DomainTypeLookupMetadata>(default);
        if (dtlm is not null) {
            var catalog = analysis.GetCatalog(dtlm.Domain);
            if (catalog is not null)
                catalog.ActionsByEntityName.TryGetValue(entity.Name, out arm);
        }
        arm ??= analysis.GetMetadata<ActionResolutionMetadata>(entity);

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

    // ── Policy resolution ─────────────────────────────────────

    /// <summary>
    /// Effective policies at a stage: prefer <see cref="StageCapabilityMetadata"/>
    /// (single composition path — DAS W2); else catalog/MTI entity+stage maps.
    /// </summary>
    public static IReadOnlyList<Policy> GetEffectivePolicies(
        this AnalysisResult analysis,
        Domain domain,
        Entity entity,
        string stageName) {
        if (analysis.TryGetStage(entity, stageName, out var stage) && stage is not null) {
            var cap = analysis.GetMetadata<StageCapabilityMetadata>(stage);
            if (cap is not null)
                return cap.View.EffectivePolicies;
        }

        var mti = analysis.GetCatalog(domain)?.Index
            ?? analysis.GetMetadata<MutationTargetIndexMetadata>(domain);
        if (mti is null) return Array.Empty<Policy>();

        var policies = new List<Policy>();
        if (mti.EntityPoliciesByEntity.TryGetValue(entity.Name, out var entityPolicies))
            policies.AddRange(entityPolicies.Values);
        if (mti.StagePoliciesByEntity.TryGetValue(entity.Name, out var stagePolicies)
            && stagePolicies.TryGetValue(stageName, out var stageScopedPolicies))
            policies.AddRange(stageScopedPolicies.Values);
        return policies;
    }

    // ── Relationship resolution ───────────────────────────────

    public static bool TryGetRelationship(this AnalysisResult analysis, string relationshipName, out Relationship? relationship) {
        RelationshipLookupMetadata? rlm = null;
        // Prefer any domain catalog on the analysis default path via MTI domain key is unknown;
        // RLM is still default-keyed.
        rlm = analysis.GetMetadata<RelationshipLookupMetadata>(default);
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
        var catalog = analysis.GetCatalog(domain);
        var rlm = catalog?.Relationships ?? analysis.GetMetadata<RelationshipLookupMetadata>(default);
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
        var dtlm = analysis.GetMetadata<DomainTypeLookupMetadata>(default);
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
        var dtlm = analysis.GetCatalog(domain)?.Types
            ?? analysis.GetMetadata<DomainTypeLookupMetadata>(default);
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
        var dtlm = analysis.GetMetadata<DomainTypeLookupMetadata>(default);
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