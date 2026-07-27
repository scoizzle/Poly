using Poly.Analysis;
using Poly.Ast.Nodes;

namespace Poly.DomainModeling.Lowering;

/// <summary>
/// Shared context for lowering passes. Bundles the subject (current-instance root),
/// optional parameter map, and the analysis result so lowering passes can consume
/// pre-computed metadata instead of re-scanning <see cref="Domain"/> collections.
///
/// When <see cref="Analysis"/> is provided, lowering reads <see cref="IAnalysisMetadata"/>
/// from it — every caller already has the <see cref="AnalysisResult"/> in hand.
/// When null, lowering falls back to the pre-Phase-0 re-scan logic.
/// </summary>
/// <param name="Subject">The Syntax AST node representing the current entity instance.</param>
/// <param name="Parameters">Optional map of parameter names to Syntax AST nodes.</param>
/// <param name="Analysis">
/// The analysis result with pre-computed metadata. When present, lowering uses
/// metadata lookups instead of scanning domain collections. Null-safe (falls
/// back to re-scan).</param>
/// <param name="UseThisReference">
/// When true, the lowered tree uses <see cref="ThisReference"/> as the instance root
/// instead of <see cref="Parameter"/>. Useful when generating C# method bodies where
/// <c>this.Property</c> is idiomatic. Defaults to false (VM-compatible mode).
/// </param>
/// <param name="ActionParameterNames">
/// When <see cref="UseThisReference"/> is true, these names are rendered as bare
/// parameters (e.g. <c>maxAmount</c>) instead of <c>this.maxAmount</c>.
/// </param>
/// <param name="LowerStageTransitions">
/// When true, <see cref="StageTransitionEffect"/> is lowered to an Assignment
/// node (<c>this.CurrentStage = Xxx</c>) instead of returning null for direct
/// execution. Used for C# code generation where transitions should be emitted
/// as property writes. Defaults to false (runtime-compatible mode).
/// </param>
/// <param name="Domain">Optional domain reference for cross-entity type resolution.</param>
/// <param name="StageEnumTypeName">
/// Optional stage enum type name for stage transition lowering. Overrides the
/// default <c>{EntityName}Stage</c> derivation — necessary for inherited entities
/// where the stage enum is defined on the root ancestor.
/// </param>
/// <param name="PostTransitionNodes">
/// Optional map of stage name to Syntax AST nodes to emit <em>after</em> the
/// <c>CurrentStage</c> assignment when lowering a transition to that stage.
/// Used for cross-entity subscription notifications in C# codegen mode.
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
public sealed record LoweringContext(
    Node Subject,
    IReadOnlyDictionary<string, Node>? Parameters = null,
    AnalysisResult? Analysis = null,
    bool UseThisReference = false,
    HashSet<string>? ActionParameterNames = null,
    bool LowerStageTransitions = false,
    Domain? Domain = null,
    string? StageEnumTypeName = null,
    IReadOnlyDictionary<string, IReadOnlyList<Node>>? PostTransitionNodes = null,
    string? SourceStageName = null,
    IReadOnlyDictionary<string, string>? EnumPropertyNames = null
);