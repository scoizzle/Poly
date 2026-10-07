using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Compile;

/// <summary>
/// Extra artifacts from the analyzed domain. Libraries register these on the
/// session builder. Contributors run only after domain analysis succeeds; an
/// analysis with Errors fails closed first (see <c>DomainSession.Lower</c>) and
/// the compiler never asks a contributor over a failed analysis.
/// Each contributed file is a <see cref="ContributedFile"/> artifact. The compiler
/// registers what <see cref="Contribute"/> returns and writes those files from the catalog.
/// </summary>
public interface IArtifactContributor {
    /// <summary>Artifacts for <paramref name="domain"/>, or an empty list when this
    /// contributor has nothing to emit for the analyzed domain.</summary>
    IReadOnlyList<Artifact> Contribute(Domain domain, AnalysisResult analysis);
}

/// <summary>
/// A text file a contributor emits. The artifact type is <see cref="Type"/>, the id
/// path is the domain name plus the file name, and the payload is the file text.
/// <see cref="DomainSession"/> declares the type with no references.
/// </summary>
public static class ContributedFile {
    public const string Type = "file";

    public static Artifact Create(Domain domain, string fileName, string text, string producer) {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(producer);
        return new Artifact(
            new ArtifactDescriptor(ArtifactId.Create([domain.Name, fileName], Type), producer),
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