using Poly.Syntax.Nodes;

namespace Poly.DomainModeling.Lowering;

/// <summary>
/// Shared context for lowering passes. Bundles the subject (current-instance root)
/// and optional parameter map so both <see cref="DomainExpressionLoweringPass"/>
/// and <see cref="EffectLoweringPass"/> see the same context.
/// </summary>
public sealed record LoweringContext(
    Node Subject,
    IReadOnlyDictionary<string, Node>? Parameters = null
);