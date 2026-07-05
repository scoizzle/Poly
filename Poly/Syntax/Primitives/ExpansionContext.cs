using Poly.Syntax.Analysis;

namespace Poly.Syntax.Primitives;

/// <summary>
/// Expansion context for AST → PrimitiveNode lowering.  Wraps the
/// <see cref="AnalysisContext"/> from the analysis pipeline and adds
/// per-expansion mutable state: slot assignment, statement depth,
/// loop boundary tracking, closure capture detection.
///
/// Every <c>Node.ToPrimitives()</c> method receives one of these,
/// replacing the previous <c>AnalysisContext</c> parameter.  Access
/// analysis metadata through <see cref="Analysis"/>.
/// </summary>
public sealed class ExpansionContext : IAnalysisMetadata {
    private readonly AnalysisContext _analysis;
    private readonly ExpansionEnvironment _env;

    /// <summary>The enclosed analysis context (type resolution, metadata, diagnostics).</summary>
    public AnalysisContext Analysis => _analysis;

    /// <summary>The expansion environment (slots, depth, captures, child scopes).</summary>
    public ExpansionEnvironment Env => _env;

    /// <summary>Creates a root expansion context from an analysis context.</summary>
    public ExpansionContext(AnalysisContext analysis) {
        _analysis = analysis;
        _env = new ExpansionEnvironment();
    }

    /// <summary>Creates a child context for lambda body expansion (new slot space).</summary>
    private ExpansionContext(AnalysisContext analysis, ExpansionEnvironment childEnv) {
        _analysis = analysis;
        _env = childEnv;
    }

    /// <summary>Create a child expansion context with a fresh slot space.
    /// Used for lambda body expansion — outer-scope slots become captures.</summary>
    public ExpansionContext CreateChildScope() =>
        new(_analysis, _env.CreateChildScope());

    // ── Convenience accessors (delegates to analysis context) ──

    public T? GetMetadata<T>(Node? node) where T : class, IAnalysisMetadata
        => _analysis.GetMetadata<T>(node);
    public void SetMetadata<T>(Node? node, T metadata) where T : class, IAnalysisMetadata
        => _analysis.SetMetadata(node, metadata);
    public Node? GetNodeReplacement(Node node) => _analysis.GetNodeReplacement(node);
    public INodeMetadataProvider Metadata => _analysis;
}