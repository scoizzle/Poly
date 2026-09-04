using Poly.Ast.Nodes;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Session + analysis + lowered module for a <see cref="Domain"/> instance.
/// Authoring <see cref="DomainSession.Analyze"/> binds the session that loaded
/// the domain's <c>uses</c> (vendor maps included). Fallback reopen uses the
/// core catalog only when nothing has bound yet.
/// </summary>
internal static class RuntimeAnalysisCache {
    private sealed class Holder {
        public required DomainSession Session { get; set; }
        public AnalysisResult? Analysis { get; set; }
        public IReadOnlyList<TypeDefinitionNode>? Module { get; set; }
        public Dictionary<string, Node?>? Operations { get; set; }
    }

    private static readonly ConditionalWeakTable<Domain, Holder> Cache = new();

    public static DomainSession Session(Domain domain) {
        ArgumentNullException.ThrowIfNull(domain);
        return GetHolder(domain).Session;
    }

    public static void Bind(Domain domain, DomainSession session, AnalysisResult? analysis = null) {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(session);
        var holder = Cache.GetValue(domain, _ => new Holder { Session = session });
        lock (holder) {
            var sessionChanged = !ReferenceEquals(holder.Session, session);
            var analysisChanged = analysis is not null && !ReferenceEquals(holder.Analysis, analysis);
            holder.Session = session;
            if (analysis is not null)
                holder.Analysis = analysis;
            if (sessionChanged || analysisChanged) {
                holder.Module = null;
                holder.Operations = null;
            }
        }
    }

    public static AnalysisResult GetOrAnalyze(Domain domain) {
        ArgumentNullException.ThrowIfNull(domain);
        var holder = GetHolder(domain);
        if (holder.Analysis is not null)
            return holder.Analysis;

        lock (holder) {
            if (holder.Analysis is not null)
                return holder.Analysis;
            var analysis = holder.Session.AnalyzeWithoutBind(domain);
            DomainModelAnalyzer.RequireCatalog(analysis, domain);
            holder.Analysis = analysis;
            return analysis;
        }
    }

    public static IReadOnlyList<TypeDefinitionNode> GetOrLower(
        Domain domain, DomainSession session, AnalysisResult analysis) {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(analysis);
        Bind(domain, session, analysis);
        var holder = GetHolder(domain);
        if (holder.Module is not null)
            return holder.Module;
        lock (holder) {
            if (holder.Module is not null)
                return holder.Module;
            holder.Module = DomainProgramProjection.ToSyntax(domain, analysis);
            return holder.Module;
        }
    }

    public static Node? GetOrLowerOperation(Domain domain, string key, Func<Node?> lower) {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(lower);
        var holder = GetHolder(domain);
        lock (holder) {
            holder.Operations ??= new Dictionary<string, Node?>(StringComparer.Ordinal);
            if (holder.Operations.TryGetValue(key, out var cached))
                return cached;
            var tree = lower();
            holder.Operations[key] = tree;
            return tree;
        }
    }

    private static Holder GetHolder(Domain domain) =>
        Cache.GetValue(domain, static d => {
            var ids = d.Extensions.Where(ExtensionCatalog.Core.Contains).ToList();
            return new Holder { Session = DomainSession.ForExtensions(ids) };
        });
}
