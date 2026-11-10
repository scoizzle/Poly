using Poly.Ast.Nodes;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Libraries.Tests;

/// <summary>
/// The generated test compilation unit for one entity. Id path is the domain name plus the entity
/// name; the payload is the <see cref="CompilationUnitNode"/> the <see cref="TestFile"/> was printed from.
/// </summary>
public static class TestTree {
    public const string Type = "test-tree";

    public static ArtifactId IdFor(Domain domain, string entityName) =>
        ArtifactId.Create([domain.Name, entityName], Type);

    public static Artifact Create(Domain domain, string entityName, CompilationUnitNode unit, string producer) =>
        new(new ArtifactDescriptor(IdFor(domain, entityName), producer), unit);
}

/// <summary>
/// The printed test source for one entity. Id path is the domain name plus the entity name; the
/// payload is the C# text. It points at the entity tree it exercises and the test tree it was
/// printed from, so "which tests cover entity X" is a catalog query over the references.
/// </summary>
public static class TestFile {
    public const string Type = "test-file";

    public static ArtifactId IdFor(Domain domain, string entityName) =>
        ArtifactId.Create([domain.Name, entityName], Type);

    public static Artifact Create(
        Domain domain, string entityName, string text, string producer, IReadOnlyList<ArtifactId> references) =>
        new(new ArtifactDescriptor(IdFor(domain, entityName), producer, references), text);

    public static string Text(Artifact artifact) {
        ArgumentNullException.ThrowIfNull(artifact);
        if (artifact.Descriptor.Id.Type != Type || artifact.Payload is not string text)
            throw new InvalidOperationException($"Artifact '{artifact.Descriptor.Id}' is not a test file.");
        return text;
    }
}

/// <summary>
/// The declared outcome for an entity the producer cannot generate a suite for. The reason names
/// the member that blocks generation. An unsupported entity gets this and no <see cref="TestFile"/>;
/// it is never an empty file that passes.
/// </summary>
public static class TestUnsupported {
    public const string Type = "test-unsupported";

    public static ArtifactId IdFor(Domain domain, string entityName) =>
        ArtifactId.Create([domain.Name, entityName], Type);

    public static Artifact Create(
        Domain domain, string entityName, string reason, string producer, IReadOnlyList<ArtifactId> references) =>
        new(new ArtifactDescriptor(IdFor(domain, entityName), producer, references), reason);

    public static string Reason(Artifact artifact) {
        ArgumentNullException.ThrowIfNull(artifact);
        if (artifact.Descriptor.Id.Type != Type || artifact.Payload is not string reason)
            throw new InvalidOperationException($"Artifact '{artifact.Descriptor.Id}' is not a test-unsupported outcome.");
        return reason;
    }
}