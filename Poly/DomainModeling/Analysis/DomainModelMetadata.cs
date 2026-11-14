using Poly.DomainModeling.Ontology;

using Action = Poly.DomainModeling.Ontology.Action;

namespace Poly.DomainModeling.Analysis;

internal sealed record DomainTypeLookupMetadata(
    Domain Domain,
    IReadOnlyDictionary<string, DomainType> Types,
    IReadOnlySet<Entity> Entities) : IAnalysisMetadata;

public sealed record ResolvedTypeReferenceMetadata(DomainType Type) : IAnalysisMetadata;

/// <summary>
/// Reverse membership index: the entity that owns an <see cref="Action"/> or
/// <see cref="Stage"/> node. Published once by <see cref="DomainCatalogPass"/>.
/// </summary>
public sealed record OwnerEntityMetadata(Entity Owner) : IAnalysisMetadata;

/// <summary>
/// Library-stamped assign conversion: lowering emits
/// <c>Invoke(Member(value, MethodName), static-member args)</c>. Temporal uses
/// this for Date onto DateTime (<c>ToDateTime(TimeOnly.MinValue)</c>).
/// </summary>
public sealed record AssignedMemberConversionArgument(string TypeName, string MemberName);

public sealed record AssignedMemberConversionMetadata(
    string MethodName,
    IReadOnlyList<AssignedMemberConversionArgument> Arguments
) : IAnalysisMetadata;

/// <summary>
/// The type of an expression, stamped by core inference after consulting session <c>ExpressionMeaning</c>
/// (core does not name library IR in its switch). <see cref="Binary"/> is the typing of an arithmetic
/// node; absent means the analyzer did not state an operator, never "operator undefined" and never
/// "unknown type". A stamp whose <see cref="Binary"/> is set is never read back as the expression's type
/// by inference or by <c>TemporalMeaning</c>.
/// </summary>
internal sealed record TypedExpressionMetadata(ExprType Type, BinaryTyping? Binary = null) : IAnalysisMetadata;

/// <summary>
/// What the analyzer states about one arithmetic node: the result type and the numeric kind.
/// Absent (<see cref="TypedExpressionMetadata.Binary"/> null) means the analyzer did not state an
/// operator for this node: never "operator undefined" (acceptance does not read it; T-07 makes it the gate),
/// never "unknown type". The stamp is first-writer-wins per node; T-07, which makes it a gate, must key or
/// recompute it per parameter scope (a shared <c>require</c> tree is walked once per scope).
/// </summary>
internal sealed record BinaryTyping(
    ExprType Result, NumericKind Kind, bool IsIntegerDivision, bool MayOverflow, bool Checked);

/// <summary>
/// What the analyzer states about one <c>match</c> (T-09a): the one type its arms unify to (<see cref="Unified"/>,
/// optional when an arm is <c>null</c> or optional) and each arm's own type, the arms in order then the <c>else</c>.
/// <c>CheckMatch</c> writes it once, from the function inference uses, when every arm is typed and unifies; lowering
/// reads it to convert each arm to the unified type and fails closed, positioned, when it is absent.
/// </summary>
internal sealed record MatchTypingMetadata(ExprType Unified, IReadOnlyList<ExprType> ArmTypes) : IAnalysisMetadata;

/// <summary>
/// The slot a <c>match</c> is assigned into, stamped by the site that knows it (an assign, a create or invoke argument, a
/// default) before the match is walked: an enum member and a Text string arm unify only into that enum.
/// </summary>
internal sealed record MatchTargetMetadata(ExprType Target) : IAnalysisMetadata;

/// <summary>
/// The domain's type model for this analysis run, stamped on the <see cref="Domain"/> node by
/// <c>ExpressionTypeAnalyzer</c>. Consumer contract: a reader runs after the <c>DomainExpressionType</c>
/// pass and fails closed when the stamp is absent.
/// </summary>
internal sealed record DomainTypeModelMetadata(DomainTypeModel Model) : IAnalysisMetadata;

public sealed record RelationshipLookupMetadata(
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, Relationship>> BySourceEntity
) : IAnalysisMetadata {
    /// <summary>
    /// Resolves a relationship by its owning (source) entity name and nav name.
    /// Relationship identity is (source entity, name) — two entities may each
    /// declare a navigation property with the same name.
    /// </summary>
    public bool TryGetRelationship(string sourceEntityName, string relationshipName, [NotNullWhen(true)] out Relationship? relationship) {
        if (BySourceEntity.TryGetValue(sourceEntityName, out var byNav)
            && byNav.TryGetValue(relationshipName, out var rel)) {
            relationship = rel;
            return true;
        }
        relationship = null;
        return false;
    }

    /// <summary>
    /// Relationships with the given name declared on any source entity. Used by
    /// diagnostics that must distinguish "exists but wrong direction/source"
    /// from "name unknown" after a source-scoped lookup misses.
    /// </summary>
    public IEnumerable<Relationship> FindByNameAcrossSources(string relationshipName) {
        foreach (var byNav in BySourceEntity.Values) {
            if (byNav.TryGetValue(relationshipName, out var rel))
                yield return rel;
        }
    }
}

public sealed record ResolvedRelationshipTargetMetadata(
    Relationship Relationship,
    Entity TargetEntity
) : IAnalysisMetadata;

public sealed record ActionResolutionMetadata(
    IReadOnlyDictionary<string, Action> EntityActions,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, Action>> StageActions
) : IAnalysisMetadata;

public sealed record RelationshipContractMetadata(
    IReadOnlyList<RelationshipContract> Contracts
) : IAnalysisMetadata;

/// <param name="IsRequired">The navigation is a required to-one (L-15a): a create must bind it, and the
/// host refuses to unlink it.</param>
/// <param name="CreateInFilledNavigation">The navigation of the target that <c>create in</c> over this
/// relationship fills with the source (<see cref="CreateInWiring.FilledNavigation"/>), or null.</param>
public sealed record RelationshipContract(
    string Name,
    string SourceEntityName,
    string TargetEntityName,
    RelationshipCardinality Cardinality,
    bool SourceOwnsTarget,
    bool IsRequired = false,
    string? CreateInFilledNavigation = null
);

/// <summary>
/// Subscription dispatch plan keyed by relationship name.
/// Published on <see cref="Stage"/> for stage-scoped <c>when</c>, and on
/// <see cref="Entity"/> for always-active entity-level <see cref="Entity.Subscriptions"/>
/// (same entry shape; entity-level bags entity plans on the entity node).
/// </summary>
public sealed record SubscriptionDispatchPlanMetadata(
    IReadOnlyDictionary<string, IReadOnlyList<SubscriptionDispatchPlanEntry>> ByRelationshipName
) : IAnalysisMetadata;

public sealed record SubscriptionDispatchPlanEntry(
    string RelationshipName,
    string SourceEntityName,
    string TargetEntityName,
    StageSubscriptionQuantifier Quantifier,
    IReadOnlySet<string> StageNames,
    IReadOnlyList<Effect> Effects,
    string? PeerBinding = null
);

public sealed record MutationTargetIndexMetadata(
    IReadOnlyDictionary<string, DomainType> TypesByName,
    IReadOnlyDictionary<string, Entity> EntitiesByName,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, Relationship>> RelationshipsByName,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, Stage>> StagesByEntity,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<Action>>> ActionsByEntity,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, Policy>> EntityPoliciesByEntity,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, Policy>>> StagePoliciesByEntity,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, Policy>>> ActionPoliciesByEntity
) : IAnalysisMetadata;