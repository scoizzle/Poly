using Poly.Analysis;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Shared semantic lookup helpers on <see cref="AnalysisResult"/>.
///
/// These provide a single entry point for downstream consumers (lowering,
/// runtime, MCP, evolution) to resolve domain semantics without re-scanning
/// domain collections. Every method fails closed — returning <c>false</c>
/// when the required metadata is absent rather than performing a fallback scan.
///
/// <para>Use these instead of direct <see cref="IAnalysisMetadata"/> lookups
/// when the consumer does not need to know which specific metadata record
/// backs the operation. Consumers that already have a concrete metadata
/// record in hand (e.g. <see cref="ActionResolutionMetadata"/>) may continue
/// to consume it directly.</para>
/// </summary>
public static class DomainSemanticLookupExtensions {

    // ── Stage resolution ──────────────────────────────────────

    /// <summary>
    /// Attempts to resolve <paramref name="stageName"/> on <paramref name="entity"/>
    /// using <see cref="EntityStructureMetadata.StageByName"/>.
    /// Returns <c>false</c> when the metadata is absent or the stage is not found.
    /// </summary>
    public static bool TryGetStage(this AnalysisResult analysis, Entity entity, string stageName, out Stage? stage) {
        var esm = analysis.GetMetadata<EntityStructureMetadata>(entity);
        if (esm?.StageByName is not null && esm.StageByName.TryGetValue(stageName, out stage))
            return true;
        stage = null;
        return false;
    }

    // ── Action resolution ─────────────────────────────────────

    /// <summary>
    /// Attempts to resolve <paramref name="actionName"/> on <paramref name="entity"/>.
    /// When <paramref name="currentStage"/> is provided, stage-scoped actions are
    /// checked first; entity-level actions are checked second.
    /// Returns <c>false</c> when the metadata is absent or the action is not found.
    /// </summary>
    public static bool TryResolveAction(
        this AnalysisResult analysis,
        Entity entity,
        string? currentStage,
        string actionName,
        out Action? action) {
        var arm = analysis.GetMetadata<ActionResolutionMetadata>(entity);
        if (arm is null) {
            action = null;
            return false;
        }

        // Stage-scoped actions take priority
        if (currentStage is not null
            && arm.StageActions.TryGetValue(currentStage, out var stageActions)
            && stageActions.TryGetValue(actionName, out var stageAction)) {
            // SA semantics: empty stage-copy + existing entity action → prefer entity action.
            // Otherwise return the stage action even if empty (metadata-only path completeness).
            //
            // Parameters are intentionally NOT part of the emptiness check:
            // AddActionToStageChange copies the entity action's parameters into the
            // stage copy, so a stage copy can carry parameters while still having no
            // effects/policies. Such a copy must fall through to the entity action —
            // otherwise the runtime silently no-ops (Phase 3 §6e).
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
    /// Returns effective policies for <paramref name="entity"/> at
    /// <paramref name="stageName"/> using <see cref="MutationTargetIndexMetadata"/>.
    /// Combines entity-level and stage-level policies from the domain-keyed metadata.
    /// Returns empty list when metadata is absent.
    /// </summary>
    public static IReadOnlyList<Policy> GetEffectivePolicies(
        this AnalysisResult analysis,
        Domain domain,
        Entity entity,
        string stageName) {
        // MTI is published on the domain node — not on default — so pass domain explicitly.
        var mti = analysis.GetMetadata<MutationTargetIndexMetadata>(domain);
        if (mti is null) return Array.Empty<Policy>();

        var policies = new List<Policy>();

        // Entity-level policies
        if (mti.EntityPoliciesByEntity.TryGetValue(entity.Name, out var entityPolicies))
            policies.AddRange(entityPolicies.Values);

        // Stage-level policies only (action-level policies are not stage-effective)
        if (mti.StagePoliciesByEntity.TryGetValue(entity.Name, out var stagePolicies)
            && stagePolicies.TryGetValue(stageName, out var stageScopedPolicies))
            policies.AddRange(stageScopedPolicies.Values);

        return policies;
    }

    // ── Relationship resolution ───────────────────────────────

    /// <summary>
    /// Attempts to resolve <paramref name="relationshipName"/> using
    /// <see cref="RelationshipLookupMetadata"/>.
    /// Returns <c>false</c> when the metadata is absent or the relationship is not found.
    /// </summary>
    public static bool TryGetRelationship(this AnalysisResult analysis, string relationshipName, out Relationship? relationship) {
        var rlm = analysis.GetMetadata<RelationshipLookupMetadata>(default);
        if (rlm is not null && rlm.Relationships.TryGetValue(relationshipName, out relationship))
            return true;
        relationship = null;
        return false;
    }

    /// <summary>
    /// Returns all outbound relationships for <paramref name="entityName"/>
    /// using <see cref="RelationshipContractMetadata"/>.
    /// Returns empty list when metadata is absent (soft-empty — no consumer
    /// yet requires fail-closed behavior here).
    /// </summary>
    public static IReadOnlyList<Relationship> GetOutboundRelationships(this AnalysisResult analysis, string entityName) {
        // Prefer RelationshipContractMetadata when available (richer shape)
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

    /// <summary>
    /// Returns all inbound relationships for <paramref name="entityName"/>
    /// using <see cref="RelationshipContractMetadata"/>.
    /// Returns empty list when metadata is absent (soft-empty — no consumer
    /// yet requires fail-closed behavior here).
    /// </summary>
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

    /// <summary>
    /// Attempts to resolve <paramref name="typeName"/> to an <see cref="Entity"/>
    /// using <see cref="DomainTypeLookupMetadata"/>.
    /// Returns <c>false</c> when the metadata is absent or the type is not an entity.
    /// </summary>
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

    /// <summary>
    /// Attempts to resolve <paramref name="typeName"/> to an <see cref="EnumType"/>
    /// using <see cref="DomainTypeLookupMetadata"/>.
    /// Returns <c>false</c> when the metadata is absent or the type is not an enum.
    /// </summary>
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