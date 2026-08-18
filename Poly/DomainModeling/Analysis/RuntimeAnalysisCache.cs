using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// One reopen for runtime/export when the caller did not keep the session.
/// Uses the core catalog (vendor ids contribute no maps). Compiler/MCP
/// paths that have a session must call <see cref="DomainSession.Analyze"/>
/// instead of coming through here.
/// </summary>
internal static class RuntimeAnalysisCache {
    private sealed class Holder {
        public required DomainSession Session { get; init; }
        public AnalysisResult? Analysis { get; set; }
    }

    private static readonly ConditionalWeakTable<Domain, Holder> Cache = new();

    public static DomainSession Session(Domain domain) {
        ArgumentNullException.ThrowIfNull(domain);
        if (Cache.TryGetValue(domain, out var holder))
            return holder.Session;

        var ids = domain.Extensions.Where(ExtensionCatalog.Core.Contains).ToList();
        var session = DomainSession.ForExtensions(ids);
        Cache.Add(domain, new Holder { Session = session });
        return session;
    }

    public static ExpressionMeaning Meaning(Domain? domain) =>
        domain is null ? ExpressionMeaning.Empty : Session(domain).Meaning;

    public static AnalysisResult GetOrAnalyze(Domain domain) {
        ArgumentNullException.ThrowIfNull(domain);
        _ = Session(domain);
        if (!Cache.TryGetValue(domain, out var holder))
            throw new InvalidOperationException("Runtime session cache missed after Session().");
        if (holder.Analysis is not null)
            return holder.Analysis;

        var analysis = holder.Session.Analyze(domain);
        DomainModelAnalyzer.RequireCatalog(analysis, domain);
        holder.Analysis = analysis;
        return analysis;
    }
}