using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Analysis;

/// <summary>This unit should emit a DbContext (vendor or host persistence).</summary>
public sealed record PersistenceSurfaceMetadata : IAnalysisMetadata;

/// <summary>Publishes <see cref="PersistenceSurfaceMetadata"/> when a persistence library is loaded.</summary>
public sealed class PersistenceSurfacePass : INodeAnalyzer {
    public const string Id = "PersistenceSurface";
    public string PassName => Id;
    public string[] Dependencies => [StoragePass.Id];

    public void Analyze(AnalysisContext context, Node node) {
        if (node is Domain domain)
            context.SetMetadata(domain, new PersistenceSurfaceMetadata());
    }
}