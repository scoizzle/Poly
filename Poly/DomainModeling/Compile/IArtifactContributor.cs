using Poly.Ast.Nodes;
using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Compile;

/// <summary>
/// Extra artifacts from the analyzed domain. Libraries register these on the
/// session builder. Contributors run only after domain analysis succeeds; an
/// analysis with Errors fails closed first (see <c>DomainSession.Lower</c>) and
/// the compiler never asks a contributor over a failed analysis.
/// Each contributed file is a <see cref="ContributedFile"/> or <see cref="HttpFile"/>
/// artifact. The compiler registers what <see cref="Contribute"/> returns and writes
/// those files from the catalog.
/// </summary>
public interface IArtifactContributor {
    /// <summary>Artifacts for <paramref name="domain"/>, or an empty list when this
    /// contributor has nothing to emit for the analyzed domain.
    /// <paramref name="catalog"/> is the compile that just ran: entity and scaffolding
    /// trees are already registered. Contributors read those trees instead of lowering again.</summary>
    IReadOnlyList<Artifact> Contribute(Domain domain, AnalysisResult analysis, ArtifactCatalog catalog);
}

/// <summary>
/// A printed C# file. The artifact type is <see cref="Type"/>, the id path is the
/// domain name plus the file name, and the payload is the file text. A printed C#
/// file points at the tree it was printed from (<c>entity</c>, <c>scaffolding</c>,
/// or <see cref="HostTree.Type"/>).
/// </summary>
public static class ContributedFile {
    public const string Type = "file";

    public static Artifact Create(
        Domain domain, string fileName, string text, string producer, ArtifactId? tree = null) {
        IReadOnlyList<ArtifactId> references = tree is null ? [] : [tree];
        return TextArtifact.Create(domain, Type, fileName, text, producer, references);
    }

    public static string FileName(Artifact artifact) => TextArtifact.FileName(artifact, Type);

    public static string Text(Artifact artifact) => TextArtifact.Text(artifact, Type);
}

/// <summary>
/// The compilation unit a host generator prints (Program.cs, a DbContext). The
/// payload is that unit. A <see cref="ContributedFile"/> points at it.
/// </summary>
public static class HostTree {
    public const string Type = "host-tree";

    public static Artifact Create(Domain domain, string name, CompilationUnitNode unit, string producer) {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(producer);
        return new Artifact(
            new ArtifactDescriptor(ArtifactId.Create([domain.Name, name], Type), producer),
            unit);
    }
}

/// <summary>
/// <c>demo.http</c>. This is the one printed artifact type with no tree behind it
/// (see <see cref="PrintedArtifactRules.TypesAllowedWithoutTreeReference"/>).
/// </summary>
public static class HttpFile {
    public const string Type = "http-file";

    public static Artifact Create(Domain domain, string fileName, string text, string producer) =>
        TextArtifact.Create(domain, Type, fileName, text, producer, []);

    public static string FileName(Artifact artifact) => TextArtifact.FileName(artifact, Type);

    public static string Text(Artifact artifact) => TextArtifact.Text(artifact, Type);
}

/// <summary>
/// Which printed artifact types may ship with no tree reference. The list is
/// <see cref="HttpFile.Type"/> only.
/// </summary>
public static class PrintedArtifactRules {
    public static readonly IReadOnlyList<string> TreeReferenceTypes = ["entity", "scaffolding", HostTree.Type];

    public static readonly IReadOnlyList<string> TypesAllowedWithoutTreeReference = [HttpFile.Type];
}

static class TextArtifact {
    public static Artifact Create(
        Domain domain,
        string type,
        string fileName,
        string text,
        string producer,
        IReadOnlyList<ArtifactId> references) {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(references);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(producer);
        return new Artifact(
            new ArtifactDescriptor(ArtifactId.Create([domain.Name, fileName], type), producer, references),
            text);
    }

    public static string FileName(Artifact artifact, string type) => Read(artifact, type).Name;

    public static string Text(Artifact artifact, string type) => Read(artifact, type).Text;

    private static (string Name, string Text) Read(Artifact artifact, string type) {
        ArgumentNullException.ThrowIfNull(artifact);
        if (artifact.Descriptor.Id.Type != type || artifact.Payload is not string text)
            throw new InvalidOperationException(
                $"Artifact '{artifact.Descriptor.Id}' is not a {type} text file.");
        return (artifact.Descriptor.Id.Segments[^1], text);
    }
}