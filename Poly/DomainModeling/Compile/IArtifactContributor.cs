using Poly.Ast.Nodes;
using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Compile;

/// <summary>
/// Extra artifacts from the analyzed domain. Libraries register these on the
/// session builder. Contributors run only after domain analysis succeeds; an
/// analysis with Errors fails closed first (see <c>DomainSession.Lower</c>) and
/// the compiler never asks a contributor over a failed analysis.
/// A contributor returns <see cref="ContributedFile"/> text and, when it printed
/// that text from a syntax tree, a <see cref="GeneratedTree"/> the file points at.
/// The compiler registers what <see cref="Contribute"/> returns and writes the
/// text files from the catalog.
/// </summary>
public interface IArtifactContributor {
    /// <summary>Artifacts for <paramref name="domain"/>, or an empty list when this
    /// contributor has nothing to emit for the analyzed domain. <paramref name="catalog"/>
    /// already holds the entity and scaffolding trees Lower registered.</summary>
    IReadOnlyList<Artifact> Contribute(Domain domain, AnalysisResult analysis, ArtifactCatalog catalog);
}

/// <summary>
/// A text file Emit or a contributor produces. The artifact type is <see cref="Type"/>,
/// the id path is the domain name plus the file name, and the payload is the file text.
/// A printed <c>.cs</c> file references the tree it was printed from.
/// </summary>
public static class ContributedFile {
    public const string Type = "file";

    public static Artifact Create(
        Domain domain,
        string fileName,
        string text,
        string producer,
        IReadOnlyList<ArtifactId>? references = null) {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(producer);
        return new Artifact(
            new ArtifactDescriptor(
                ArtifactId.Create([domain.Name, fileName], Type),
                producer,
                references ?? []),
            text);
    }

    public static string FileName(Artifact artifact) => Read(artifact).Name;

    public static string Text(Artifact artifact) => Read(artifact).Text;

    private static (string Name, string Text) Read(Artifact artifact) {
        ArgumentNullException.ThrowIfNull(artifact);
        if (artifact.Descriptor.Id.Type != Type || artifact.Payload is not string text)
            throw new InvalidOperationException(
                $"Artifact '{artifact.Descriptor.Id}' is not a contributed text file.");
        return (artifact.Descriptor.Id.Segments[^1], text);
    }
}

/// <summary>
/// The compilation unit a generator built before printing it. The artifact type is
/// <see cref="Type"/>, the id path is the domain name plus the printed file name,
/// and the payload is the <see cref="CompilationUnitNode"/>. The printed
/// <see cref="ContributedFile"/> points at <see cref="IdFor"/>.
/// </summary>
public static class GeneratedTree {
    public const string Type = "tree";

    public static ArtifactId IdFor(Domain domain, string fileName) {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        return ArtifactId.Create([domain.Name, fileName], Type);
    }

    public static Artifact Create(Domain domain, string fileName, CompilationUnitNode unit, string producer) {
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentException.ThrowIfNullOrWhiteSpace(producer);
        return new Artifact(new ArtifactDescriptor(IdFor(domain, fileName), producer), unit);
    }
}