using Poly.Analysis;
using Poly.Ast.Nodes;
using Poly.DomainModeling.Analysis;

namespace Poly.DomainModeling;

/// <summary>
/// Per-session meaning tables for library-owned expressions. Empty when the unit
/// did not load the owning library — pack IR then fails closed at rewrite/lower/check.
/// </summary>
public sealed class ExpressionMeaning {
    public static ExpressionMeaning Empty { get; } = new();

    public ExpressionDispatchRegistry<DomainExpression> Rewrite { get; } = new();

    public ExpressionDispatchRegistry<Node> Lowering { get; } = new();

    internal ExpressionDispatchRegistry<ExpressionTypeAnalyzer.TypeCategory> Inference { get; } = new();

    public ExpressionTypeCheckRegistry Checks { get; } = new();

    public ExpressionDefaultResolverRegistry Defaults { get; } = new();

    /// <summary>Meaning for a domain's declared extensions (core catalog).</summary>
    public static ExpressionMeaning For(Domain? domain) =>
        domain is null ? Empty : DomainSession.Open(domain).Meaning;
}