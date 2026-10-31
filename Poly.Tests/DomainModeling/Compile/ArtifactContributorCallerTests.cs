using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;

using Artifact = Poly.DomainModeling.Compile.Artifact;

namespace Poly.Tests.DomainModeling.Compile;

/// <summary>
/// S9: <see cref="DomainSession.Emit"/> runs each session artifact contributor
/// once and returns its host files. Callers that never construct
/// <c>DslCompiler</c> still receive them. A second Emit of the same compilation
/// lists each host file once.
/// </summary>
public sealed class ArtifactContributorCallerTests {
    private const string Spotless = """
        domain Spotless
        Item: entity {
          Name: Text
          Open: stage { }
        }
        """;

    [Test]
    public async Task Export_WithoutDslCompiler_StillGetsHostFiles() {
        var contributor = new HostFileContributor();
        var (domain, analysis, session) = Evolve(Spotless, contributor);

        var files = session.Emit(domain, analysis);

        await Assert.That(files.Select(f => f.FileName).SequenceEqual(["Item.cs", "Poly.Types.cs", "Host.txt"])).IsTrue();
        var host = files.Single(f => f.FileName == "Host.txt");
        await Assert.That(host.Source).IsEqualTo("host from Spotless");
        await Assert.That(contributor.Calls).IsEqualTo(1);

        var artifact = session.ArtifactCatalog.Artifacts.Single(a =>
            a.Descriptor.Id.Type == ContributedFile.Type && ContributedFile.FileName(a) == "Host.txt");
        await Assert.That(artifact.Descriptor.Producer).IsEqualTo(nameof(HostFileContributor));
        await Assert.That(ContributedFile.Text(artifact)).IsEqualTo(host.Source);
        await Assert.That(session.ArtifactCatalog.Find(GeneratedTree.IdFor(domain, "Host.txt"))).IsNotNull();
    }

    [Test]
    public async Task Emit_NoContributors_ReturnsOnlyModuleFiles() {
        var (domain, analysis, session) = SliceCProducerLoopCatalogTests.Evolve(Spotless);
        await Assert.That(session.Artifacts.Count).IsEqualTo(0);

        var files = session.Emit(domain, analysis);

        await Assert.That(files.Select(f => f.FileName).SequenceEqual(["Item.cs", "Poly.Types.cs"])).IsTrue();
    }

    [Test]
    public async Task Emit_Twice_SameCompilation_DoesNotDuplicateHostFiles() {
        var contributor = new HostFileContributor();
        var (domain, analysis, session) = Evolve(Spotless, contributor);

        var first = session.Emit(domain, analysis);
        var second = session.Emit(domain, analysis);

        await Assert.That(first.Count(f => f.FileName == "Host.txt")).IsEqualTo(1);
        await Assert.That(second.Count(f => f.FileName == "Host.txt")).IsEqualTo(1);
        await Assert.That(second.Select(f => f.FileName).SequenceEqual(first.Select(f => f.FileName))).IsTrue();
        await Assert.That(session.ArtifactCatalog.Artifacts.Count(a =>
            a.Descriptor.Id.Type == ContributedFile.Type && ContributedFile.FileName(a) == "Host.txt")).IsEqualTo(1);
        await Assert.That(contributor.Calls).IsEqualTo(2);
    }

    private sealed class HostFileContributor : IArtifactContributor {
        public int Calls { get; private set; }

        public IReadOnlyList<Artifact> Contribute(Domain domain, AnalysisResult analysis, ArtifactCatalog catalog) {
            Calls++;
            var tree = GeneratedTree.Create(
                domain,
                "Host.txt",
                new CompilationUnitNode([], null, [], null),
                nameof(HostFileContributor));
            return [
                tree,
                ContributedFile.Create(
                    domain,
                    "Host.txt",
                    $"host from {domain.Name}",
                    nameof(HostFileContributor),
                    [tree.Descriptor.Id]),
            ];
        }
    }

    private sealed class HostLibrary(HostFileContributor contributor) : IDomainLibrary {
        public string Id => "host-file";

        public void Register(SessionBuilder builder) => builder.AddArtifactContributor(contributor);
    }

    private static (Domain Domain, AnalysisResult Analysis, DomainSession Session) Evolve(
        string poly, HostFileContributor contributor) {
        var catalog = ExtensionCatalog.Core.With(new HostLibrary(contributor));
        var seed = ExtensionCatalog.ProductAuthoring.Append("host-file").ToArray();
        var session = DomainSession.ForSource(poly, seed, catalog);
        var result = new DomainEvolution(new Domain("_", [])).Apply(
            new PolyDslParser(poly, session).Parse(), session: session);
        if (!result.Succeeded)
            throw new InvalidOperationException(result.FailureSummary ?? "evolution rejected the domain");
        return (result.Root, result.Analysis, session);
    }
}