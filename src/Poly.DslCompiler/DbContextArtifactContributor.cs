using Poly.Analysis;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.Interpretation.CSharp;

namespace Poly.DslCompiler;

/// <summary>
/// Persistence host call-site: emits <c>{Domain}DbContext.cs</c> from the
/// persistence surface bag. Registered at Load when persistence/sqlite/sqlserver
/// is loaded — not invented mid-compile from bags.
/// </summary>
public sealed class DbContextArtifactContributor : IArtifactContributor {
    public IReadOnlyList<Artifact> Contribute(Domain domain, AnalysisResult analysis, ArtifactCatalog catalog) {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(analysis);

        if (analysis.GetMetadata<PersistenceSurfaceMetadata>(domain) is null)
            return [];

        var storageModel = analysis.GetMetadata<StorageMappingMetadata>(domain)?.Storage
            ?? throw new InvalidOperationException(
                "Infrastructure pipeline did not produce storage mapping metadata.");

        var dbContextName = $"{domain.Name}DbContext";
        var fileName = $"{dbContextName}.cs";
        var implementsStoragePort = new CatalogIndex(catalog).RequiresStoragePort;
        var unit = new DbContextGenerator(domain, storageModel, implementsStoragePort: implementsStoragePort)
            .GenerateCompilationUnit();
        var tree = GeneratedTree.Create(domain, fileName, unit, nameof(DbContextArtifactContributor));
        var source = new CSharpWriter().Write(unit);
        return [
            tree,
            ContributedFile.Create(
                domain,
                fileName,
                source,
                nameof(DbContextArtifactContributor),
                [tree.Descriptor.Id]),
        ];
    }
}