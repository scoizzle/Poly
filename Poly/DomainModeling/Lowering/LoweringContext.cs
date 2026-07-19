using Poly.Syntax.Nodes;

namespace Poly.DomainModeling.Lowering;

/// <summary>
/// Shared context for lowering passes. Bundles the subject (current-instance root)
/// and optional parameter map so both <see cref="DomainExpressionLoweringPass"/>
/// and <see cref="EffectLoweringPass"/> see the same context.
/// </summary>
/// <param name="Subject">The Syntax AST node representing the current entity instance.</param>
/// <param name="Parameters">Optional map of parameter names to Syntax AST nodes.</param>
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
public sealed record LoweringContext(
    Node Subject,
    IReadOnlyDictionary<string, Node>? Parameters = null,
    bool UseThisReference = false,
    HashSet<string>? ActionParameterNames = null,
    bool LowerStageTransitions = false
);