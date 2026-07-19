using Poly.DomainModeling.Analysis;
using Poly.Syntax.Analysis;

namespace Poly.DomainModeling.Queries;

// ── Record types for query results ────────────────────────────────────────

/// <summary>
/// High-level overview of a domain model: name and type/relationship counts.
/// Stage transitions are the authorable observable — there is no Event surface.
/// </summary>
public sealed record DomainOverview(
    string Name,
    int EntityCount,
    int RelationshipCount,
    int PrimitiveTypeCount,
    int ValueTypeCount
);

/// <summary>
/// Detailed projection of an entity for inspection and agent queries.
/// </summary>
public sealed record EntityDetail(
    string Name,
    IReadOnlyList<PropertyDetail> Properties,
    IReadOnlyList<StageDetail> Stages,
    IReadOnlyList<ActionDetail> Actions,
    IReadOnlyList<PolicyDetail> Policies,
    string? ParentEntityName,
    IReadOnlyList<NavigationDetail> Navigations
);

/// <summary>
/// Lightweight navigation property info for query results.
/// Shows source and target views — the relationship is a first-class
/// Domain object, not duplicated on the entity.
/// </summary>
public sealed record NavigationDetail(
    string RelationshipName,
    string RelatedEntityName,
    string Role,          // "Source" or "Target"
    string Cardinality,   // e.g. "OneToOne", "OneToMany"
    bool SourceOwnsTarget
);

/// <summary>
/// Lightweight property info for query results.
/// </summary>
public sealed record PropertyDetail(
    string Name,
    string TypeName,
    int ConstraintCount
);

/// <summary>
/// Lightweight subscription info for query results.
/// </summary>
public sealed record SubscriptionDetail(
    string RelationshipName,
    IReadOnlyList<string> StageNames,
    string Quantifier,
    int EffectCount
);

/// <summary>
/// Lightweight stage info for query results.
/// </summary>
public sealed record StageDetail(
    string Name,
    IReadOnlyList<string> ActionNames,
    IReadOnlyList<string> PolicyNames,
    IReadOnlyList<SubscriptionDetail> Subscriptions
);

/// <summary>
/// Lightweight action info for query results.
/// </summary>
public sealed record ActionDetail(
    string Name,
    IReadOnlyList<string> ParameterNames,
    IReadOnlyList<string> PolicyNames,
    int EffectCount
);

/// <summary>
/// Lightweight policy info for query results.
/// </summary>
public sealed record PolicyDetail(
    string Name
);

/// <summary>
/// Summarized analysis diagnostics for agent consumption.
/// </summary>
public sealed record AnalysisSummary(
    int ErrorCount,
    int WarningCount,
    int InfoCount,
    bool HasStructuralFailure,
    IReadOnlyList<string> Messages
);

/// <summary>
/// Lightweight relationship info for query results.
/// </summary>
public sealed record RelationshipSummary(
    string Name,
    string SourceEntityName,
    string TargetEntityName,
    string Cardinality,
    bool SourceOwnsTarget
);

// ── Query helpers ─────────────────────────────────────────────────────────

/// <summary>
/// Model-optimized query projections over a <see cref="Domain"/>.
///
/// These are pure functions — no session, no workspace, no MCP types.
/// Consumers (tests, MCP tools, UI) all use the same projection surface.
/// </summary>
public static class DomainQueries {
    /// <summary>
    /// Returns a high-level overview of the domain.
    /// </summary>
    public static DomainOverview Overview(Domain domain) {
        ArgumentNullException.ThrowIfNull(domain);

        var entities = domain.Types.OfType<Entity>().ToList();
        var primitives = domain.Types.OfType<PrimitiveType>().ToList();
        var valueTypes = domain.Types.OfType<ValueType>().ToList();
        return new DomainOverview(
            Name: domain.Name,
            EntityCount: entities.Count,
            RelationshipCount: domain.Relationships.Count,
            PrimitiveTypeCount: primitives.Count,
            ValueTypeCount: valueTypes.Count
        );
    }

    /// <summary>
    /// Returns a detailed projection of an entity by name, or null if not found.
    /// When <paramref name="metadata"/> is provided, enriches the result with
    /// analysis metadata: inheritance-aware members from <see cref="EffectiveMemberMetadata"/>
    /// and stage capabilities from <see cref="StageCapabilityMetadata"/>.
    /// </summary>
    public static EntityDetail? GetEntity(Domain domain, string entityName, INodeMetadataProvider? metadata = null) {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityName);

        var entity = domain.Types.OfType<Entity>()
            .FirstOrDefault(e => string.Equals(e.Name, entityName, StringComparison.Ordinal));

        if (entity is null)
            return null;

        // Read inheritance-aware members from analysis metadata when available
        var effectiveMemberMeta = metadata?.GetMetadata<EffectiveMemberMetadata>(entity);

        var properties = (effectiveMemberMeta?.EffectiveProperties ?? entity.Properties)
            .Select(p => new PropertyDetail(p.Name, p.Type.TypeName, p.Constraints.Count))
            .ToList();

        var actions = (effectiveMemberMeta?.EffectiveActions ?? entity.Actions)
            .Select(a => new ActionDetail(
                a.Name,
                a.Parameters.Select(p => p.Name).ToList(),
                a.Policies.Select(p => p.Name).ToList(),
                a.Effects.Count))
            .ToList();

        var policies = (effectiveMemberMeta?.EffectivePolicies ?? entity.Policies)
            .Select(p => new PolicyDetail(p.Name))
            .ToList();

        // Stages: use effective stages from metadata, but enrich with StageCapabilityMetadata
        // for effective action/policy lists when available
        var stages = (effectiveMemberMeta?.EffectiveStages ?? entity.Stages)
            .Select(s => {
                var stageCap = metadata?.GetMetadata<StageCapabilityMetadata>(s);
                var effectivePolicyNames = stageCap is not null
                    ? stageCap.View.EffectivePolicies.Select(p => p.Name).ToList()
                    : s.Policies.Select(p => p.Name).ToList();
                return new StageDetail(
                    s.Name,
                    s.Actions.Select(a => a.Name).ToList(),
                    effectivePolicyNames,
                    s.Subscriptions.Select(sub => new SubscriptionDetail(
                        sub.RelationshipName,
                        sub.StageNames,
                        sub.Quantifier.ToString(),
                        sub.Effects.Count)).ToList()
                );
            })
            .ToList();

        return new EntityDetail(
            Name: entity.Name,
            Properties: properties,
            Stages: stages,
            Actions: actions,
            Policies: policies,
            ParentEntityName: entity.ParentEntityName,
            Navigations: domain.Relationships
                .Where(r => string.Equals(r.Source.TypeName, entity.Name, StringComparison.Ordinal)
                         || string.Equals(r.Target.TypeName, entity.Name, StringComparison.Ordinal))
                .Select(r => new NavigationDetail(
                    r.Name,
                    string.Equals(r.Source.TypeName, entity.Name, StringComparison.Ordinal)
                        ? r.Target.TypeName : r.Source.TypeName,
                    string.Equals(r.Source.TypeName, entity.Name, StringComparison.Ordinal)
                        ? "Source" : "Target",
                    r.Cardinality.ToString(),
                    r.SourceOwnsTarget))
                .ToList()
        );
    }

    /// <summary>
    /// Lists the names of all entities in the domain.
    /// </summary>
    public static IReadOnlyList<string> ListEntities(Domain domain) {
        ArgumentNullException.ThrowIfNull(domain);
        return domain.Types.OfType<Entity>().Select(e => e.Name).ToList();
    }

    /// <summary>
    /// Lists the names of all primitive types in the domain.
    /// </summary>
    public static IReadOnlyList<string> ListPrimitives(Domain domain) {
        ArgumentNullException.ThrowIfNull(domain);
        return domain.Types.OfType<PrimitiveType>().Select(p => p.Name).ToList();
    }

    /// <summary>
    /// Lists all relationships in the domain with source, target, cardinality, and ownership.
    /// </summary>
    public static IReadOnlyList<RelationshipSummary> ListRelationships(Domain domain) {
        ArgumentNullException.ThrowIfNull(domain);
        return domain.Relationships.Select(r => new RelationshipSummary(
            r.Name,
            r.Source.TypeName,
            r.Target.TypeName,
            r.Cardinality.ToString(),
            r.SourceOwnsTarget
        )).ToList();
    }

    /// <summary>
    /// Summarizes an <see cref="AnalysisResult"/> into a concise agent-friendly form.
    /// </summary>
    public static AnalysisSummary GetAnalysisSummary(AnalysisResult analysis) {
        ArgumentNullException.ThrowIfNull(analysis);

        var errors = analysis.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        var warnings = analysis.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Warning)
            .ToList();

        var infos = analysis.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Information)
            .ToList();

        return new AnalysisSummary(
            ErrorCount: errors.Count,
            WarningCount: warnings.Count,
            InfoCount: infos.Count,
            HasStructuralFailure: analysis.HasStructuralFailure,
            Messages: errors.Concat(warnings)
                .Take(10)
                .Select(d => $"[{d.Severity}] {d.Message}")
                .ToList()
        );
    }

    /// <summary>
    /// Flattens all <see cref="StageSubscription"/> instances across every stage of the given entity.
    /// Useful for analyzers that need to inspect subscriptions without iterating stages manually.
    /// </summary>
    public static IReadOnlyList<StageSubscription> GetStageSubscriptions(Entity entity) {
        ArgumentNullException.ThrowIfNull(entity);
        return entity.Stages
            .SelectMany(s => s.Subscriptions)
            .ToList();
    }
}