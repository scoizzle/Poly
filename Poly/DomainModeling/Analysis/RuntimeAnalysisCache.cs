using System.Runtime.CompilerServices;

using Poly.Analysis;

namespace Poly.DomainModeling.Analysis;

internal static class RuntimeAnalysisCache {
    private sealed class Holder {
        public required AnalysisResult Analysis { get; init; }
    }

    private static readonly ConditionalWeakTable<Domain, Holder> Cache = new();

    public static AnalysisResult GetOrAnalyze(Domain domain) {
        ArgumentNullException.ThrowIfNull(domain);
        if (Cache.TryGetValue(domain, out var holder)) {
            return holder.Analysis;
        }

        // Require catalog for domain-bound runtime when the tree is analyzable.
        var analysis = DomainModelAnalyzer.AnalyzeRequiringCatalog(domain);
        Cache.Add(domain, new Holder { Analysis = analysis });
        return analysis;
    }
}