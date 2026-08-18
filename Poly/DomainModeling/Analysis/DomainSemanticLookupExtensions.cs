using Poly.Analysis;
using Poly.DomainModeling.Dispatch;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Contract;
using Poly.DomainModeling.Runtime;

using Action = Poly.DomainModeling.Ontology.Action;
using Add = Poly.DomainModeling.Ontology.Add;
using And = Poly.DomainModeling.Ontology.And;
using Divide = Poly.DomainModeling.Ontology.Divide;
using Multiply = Poly.DomainModeling.Ontology.Multiply;
using Not = Poly.DomainModeling.Ontology.Not;
using Or = Poly.DomainModeling.Ontology.Or;
using PrimitiveType = Poly.DomainModeling.Ontology.PrimitiveType;
using Subtract = Poly.DomainModeling.Ontology.Subtract;
using ValueType = Poly.DomainModeling.Ontology.ValueType;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Shared semantic lookup helpers over analysis metadata.
/// Name lookups read <see cref="DomainCatalogMetadata"/> (type/relationship maps
/// on <c>default</c> are the same instances). Effective policies/actions read
/// <see cref="StageCapabilityMetadata"/> only.
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
    /// Catalog type lookup. Domain-keyed reads the catalog; otherwise the
    /// default alias written by <see cref="DomainCatalogPass"/> (same instance).
    /// </summary>
    internal static DomainTypeLookupMetadata? GetTypeLookup(
        this INodeMetadataProvider analysis,
        Domain? domain = null) {
        if (domain is not null)
            return analysis.GetCatalog(domain)?.Types;
        return analysis.GetMetadata<DomainTypeLookupMetadata>(default);
    }

    /// <summary>
    /// Catalog relationship lookup. Domain-keyed reads the catalog; otherwise the
    /// default alias written by <see cref="DomainCatalogPass"/> (same instance).
    /// </summary>
    internal static RelationshipLookupMetadata? GetRelationshipLookup(
        this INodeMetadataProvider analysis,
        Domain? domain = null) {
        if (domain is not null)
            return analysis.GetCatalog(domain)?.Relationships;
        return analysis.GetMetadata<RelationshipLookupMetadata>(default);
    }

    /// <summary>
    /// Relationships authored on <paramref name="entity"/> (its source-owned
    /// navigation properties), resolved from the analysis catalog. Relationship
    /// semantics are analysis-only: returns empty when the catalog is absent.
    /// </summary>
    public static IReadOnlyList<Relationship> GetRelationships(
        this INodeMetadataProvider analysis,
        Entity entity) {
        var rlm = analysis.GetRelationshipLookup();
        if (rlm is not null && rlm.BySourceEntity.TryGetValue(entity.Name, out var byNav))
            return byNav.Values.ToList();
        return Array.Empty<Relationship>();
    }

    /// <summary>
    /// Flat relationship list across the domain (entity order, then nav order),
    /// resolved from the analysis catalog. Returns empty when the catalog is absent.
    /// </summary>
    public static IReadOnlyList<Relationship> GetAllRelationships(
        this INodeMetadataProvider analysis,
        Domain domain) {
        var rlm = analysis.GetRelationshipLookup(domain);
        if (rlm is null)
            return Array.Empty<Relationship>();
        var result = new List<Relationship>();
        foreach (var entity in domain.Types.OfType<Entity>())
            if (rlm.BySourceEntity.TryGetValue(entity.Name, out var byNav))
                result.AddRange(byNav.Values);
        return result;
    }

    // ── Stage resolution ──────────────────────────────────────

    public static bool TryGetStage(this INodeMetadataProvider analysis, Entity entity, string stageName, out Stage? stage) {
        var lookup = analysis.GetTypeLookup();
        if (lookup is not null) {
            var catalog = analysis.GetCatalog(lookup.Domain);
            if (catalog is not null
                && catalog.Index.StagesByEntity.TryGetValue(entity.Name, out var stages)
                && stages.TryGetValue(stageName, out stage))
                return true;
        }
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
    /// Entity structure published for <paramref name="entity"/>, or null.
    /// </summary>
    public static EntityStructureMetadata? GetStructure(
        this INodeMetadataProvider analysis,
        Entity entity) =>
        analysis.GetMetadata<EntityStructureMetadata>(entity);

    /// <summary>
    /// Effective policies at a stage. Capability bag only — no catalog recomposition.
    /// </summary>
    public static IReadOnlyList<Policy> GetEffectivePolicies(
        this AnalysisResult analysis,
        Domain domain,
        Entity entity,
        string stageName) {
        _ = domain;
        if (!analysis.TryGetStage(entity, stageName, out var stage) || stage is null)
            return Array.Empty<Policy>();

        return analysis.GetMetadata<StageCapabilityMetadata>(stage)?.View.EffectivePolicies
            ?? [];
    }

    /// <summary>
    /// Effective actions at a stage. Capability bag only — stage-local names.
    /// Entity-level actions stay on <see cref="TryResolveAction"/> for dispatch.
    /// </summary>
    public static IReadOnlyList<Action> GetEffectiveActions(
        this AnalysisResult analysis,
        Domain domain,
        Entity entity,
        string stageName) {
        _ = domain;
        if (!analysis.TryGetStage(entity, stageName, out var stage) || stage is null)
            return Array.Empty<Action>();

        var cap = analysis.GetMetadata<StageCapabilityMetadata>(stage);
        if (cap is null)
            return [];

        var byName = stage.Actions
            .GroupBy(a => a.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);
        var actions = new List<Action>(cap.View.EffectiveActions.Count);
        foreach (var view in cap.View.EffectiveActions) {
            if (byName.TryGetValue(view.ActionName, out var action))
                actions.Add(action);
        }
        return actions;
    }

    // ── Relationship resolution ───────────────────────────────

    /// <summary>
    /// Resolves a relationship by its owning (source) entity name and nav name.
    /// Relationship identity is (source entity, name) — the same nav name may be
    /// declared on multiple source entities.
    /// </summary>
    public static bool TryGetRelationship(this AnalysisResult analysis, string sourceEntityName, string relationshipName, out Relationship? relationship) {
        var rlm = analysis.GetRelationshipLookup();
        if (rlm is not null)
            return rlm.TryGetRelationship(sourceEntityName, relationshipName, out relationship);
        relationship = null;
        return false;
    }

    /// <summary>
    /// Resolves a relationship using catalog when <paramref name="domain"/> is provided.
    /// </summary>
    public static bool TryGetRelationship(
        this AnalysisResult analysis,
        Domain domain,
        string sourceEntityName,
        string relationshipName,
        out Relationship? relationship) {
        var rlm = analysis.GetRelationshipLookup(domain);
        if (rlm is not null)
            return rlm.TryGetRelationship(sourceEntityName, relationshipName, out relationship);
        relationship = null;
        return false;
    }

    public static IReadOnlyList<Relationship> GetOutboundRelationships(this AnalysisResult analysis, string entityName) {
        var rcm = analysis.GetMetadata<RelationshipContractMetadata>(default);
        if (rcm is not null) {
            return rcm.Contracts
                .Where(c => string.Equals(c.SourceEntityName, entityName, StringComparison.Ordinal))
                .Select(c => {
                    analysis.TryGetRelationship(c.SourceEntityName, c.Name, out var rel);
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
                    analysis.TryGetRelationship(c.SourceEntityName, c.Name, out var rel);
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