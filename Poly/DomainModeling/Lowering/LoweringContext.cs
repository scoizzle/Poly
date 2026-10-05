using Poly.DomainModeling.Meaning;
using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Lowering;

/// <summary>
/// Shared context for lowering passes. Bundles the subject (current-instance root),
/// optional parameter map, and analysis metadata so lowering **passes** can consume
/// pre-computed bags instead of re-scanning <see cref="Domain"/> collections.
/// The Syntax they emit does not carry bag types — bags are lowering input only.
///
/// When <see cref="Analysis"/> is provided, lowering reads <see cref="IAnalysisMetadata"/>
/// via <see cref="INodeMetadataProvider.GetMetadata{T}"/> (typically an
/// <see cref="AnalysisResult"/>). When null, lowering falls back to re-scan logic.
/// </summary>
/// <param name="Subject">The Syntax AST node representing the current entity instance.</param>
/// <param name="Parameters">
/// Optional map of parameter names to Syntax AST nodes. Used for
/// <c>ParameterAccess</c> and for path-prefix roots that should resolve as
/// parameter subjects (e.g. peer binders in C# subscription handlers:
/// <c>order Code</c> → <c>order.Code</c>, not <c>this.order.Code</c>).
/// Does not rewrite bag values — VM peer binding remains a separate pre-lower rewrite.
/// </param>
/// <param name="Analysis">
/// Metadata provider with pre-computed analysis bags. When present, lowering uses
/// provider lookups instead of scanning domain collections. Null-safe (falls
/// back to re-scan).</param>
/// <param name="ActionParameterNames">
/// These names are rendered as bare parameters (e.g. <c>maxAmount</c>) instead of
/// <c>this.maxAmount</c>. The instance root is <see cref="Subject"/> — pass
/// <see cref="ThisReference"/> for module method bodies.
/// </param>
/// <param name="Domain">Optional domain reference for cross-entity type resolution.</param>
/// <param name="StageEnumTypeName">
/// Optional stage enum type name for stage transition lowering. Overrides the
/// default <c>{EntityName}Stage</c> derivation — necessary for inherited entities
/// where the stage enum is defined on the root ancestor.
/// </param>
/// <param name="PostTransitionNotifyStages">
/// Optional set of stage names that have subscription notify methods. When a
/// transition targets one of these stages, <see cref="EffectLoweringPass"/>
/// captures <c>CurrentStage</c> before the assign and emits
/// <c>Notify{Stage}Subscribers(previousStageN)</c> after it. The pass builds
/// that invoke; callers only name the watched stages.
/// </param>
/// <param name="SourceStageName">
/// Optional name of the source stage from which a transition originates.
/// When set, exit effects of the source stage are emitted before the
/// target stage's entry effects.
/// </param>
/// <param name="EnumPropertyNames">
/// Optional map from property name to enum type name. When present, literal
/// comparisons against enum-typed properties emit qualified member access
/// (e.g. <c>PatronStatus.Active</c>) instead of string literals.
/// </param>
/// <param name="NavigationNameResolver">
/// Optional mapper from a DSL relationship/navigation name to the generated C#
/// member name. The exporter emits pascal-cased nav properties
/// (<c>compilations</c> → <c>Compilations</c>) while DSL expressions use the
/// camelCase name; this resolver is the single source of truth so expression
/// lowering (property reads, <c>Rel exists</c>, path-prefix) and the exporter
/// agree on the member name. Falls back to identity when null.
/// </param>
/// <param name="IsCollectionNavigation">
/// Optional predicate answering whether a DSL relationship/navigation name is a
/// collection (<c>many</c>) on the current subject entity. The C# export uses it
/// to lower <c>Rel exists</c> to a <c>.Count != 0</c> check (runtime store-link
/// presence) instead of a never-null <c>collection != null</c>.
/// </param>
/// <param name="PropertyTypeResolver">
/// Optional mapper from a property name to its domain type name. Used to lower
/// date arithmetic (<c>DueDate + 14</c> → <c>DueDate.AddDays(...)</c>) in every
/// expression context (policies, if conditions, initializers), not just assign.
/// </param>
/// <param name="Names">
/// Shared generator of unique local names for one method. Passes built from
/// this context via <c>with</c> keep the same instance.
/// </param>
/// <param name="SourceEntityName">
/// Entity used to resolve relationship targets and enum literals. At the root
/// this is the source entity. For a path-prefix hop or a quantifier body, only
/// this name and <see cref="EnumPropertyNames"/> switch to the hop or quantifier
/// target. <see cref="PropertyTypeResolver"/>, <see cref="NavigationNameResolver"/>
/// and <see cref="IsCollectionNavigation"/> stay scoped to the source entity, and
/// binder roots (parameter-backed path-prefix roots) keep the source name and
/// enum map. Relationship targets resolve on this entity, then on each nested
/// target. A missing name yields no target (no domain-wide scan).
/// </param>
public sealed record LoweringContext(
    Node Subject,
    IReadOnlyDictionary<string, Node>? Parameters = null,
    INodeMetadataProvider? Analysis = null,
    HashSet<string>? ActionParameterNames = null,
    Domain? Domain = null,
    string? StageEnumTypeName = null,
    IReadOnlySet<string>? PostTransitionNotifyStages = null,
    string? SourceStageName = null,
    IReadOnlyDictionary<string, string>? EnumPropertyNames = null,
    Func<string, string>? NavigationNameResolver = null,
    Func<string, bool>? IsCollectionNavigation = null,
    Func<string, string?>? PropertyTypeResolver = null,
    Node? ActionResultType = null,
    ExpressionMeaning? Meaning = null,
    ExpressionFormRegistry? Forms = null,
    LocalNames? Names = null,
    string? SourceEntityName = null
);
