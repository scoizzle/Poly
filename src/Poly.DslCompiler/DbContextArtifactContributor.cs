using Poly.Analysis;
using Poly.DomainModeling.Analysis;
using Poly.Interpretation.CSharp;

namespace Poly.DslCompiler;

/// <summary>
/// Persistence host call-site: emits <c>{Domain}DbContext.cs</c> from the
/// persistence surface bag. Registered at Load when persistence/sqlite/sqlserver
/// is loaded — not invented mid-compile from bags.
/// </summary>
public sealed class DbContextArtifactContributor : IArtifactContributor {
    public IReadOnlyList<Artifact> Contribute(Domain domain, AnalysisResult analysis, ArtifactCatalog catalog) {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(analysis);

        if (analysis.GetMetadata<PersistenceSurfaceMetadata>(domain) is null)
            return [];

        var storageModel = analysis.GetMetadata<StorageMappingMetadata>(domain)?.Storage
            ?? throw new InvalidOperationException(
                "Infrastructure pipeline did not produce storage mapping metadata.");

        var dbContextName = $"{domain.Name}DbContext";
        var unit = new DbContextGenerator(domain, storageModel).GenerateCompilationUnit();
        var source = new CSharpGenerator().Generate(unit);
        var tree = HostTree.Create(domain, dbContextName, unit, nameof(DbContextArtifactContributor));
        return [
            tree,
            ContributedFile.Create(
                domain, $"{dbContextName}.cs", source, nameof(DbContextArtifactContributor), tree.Descriptor.Id),
        ];
    }
}