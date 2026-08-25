using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Analysis;

/// <summary>This unit should emit a process door (Program.cs + demo.http).</summary>
public sealed record HttpSurfaceMetadata : IAnalysisMetadata;

/// <summary>Publishes <see cref="HttpSurfaceMetadata"/> when <c>uses http</c> is loaded.</summary>
public sealed class HttpSurfacePass : INodeAnalyzer {
    public const string Id = "HttpSurface";
    public string PassName => Id;
    public string[] Dependencies => [CapabilityAnalyzer.Id, OwnershipAggregatePass.Id, StoragePass.Id];

    public void Analyze(AnalysisContext context, Node node) {
        if (node is Domain domain)
            context.SetMetadata(domain, new HttpSurfaceMetadata());
    }
}