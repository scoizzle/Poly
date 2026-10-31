using Poly.DomainModeling.Compile;
using Poly.Interpretation.CSharp;

namespace Poly.Tests.DomainModeling.Compile;

/// <summary>
/// S5r: <see cref="DomainSession.Compile"/> produces one <see cref="CompiledDomain"/>.
/// Print consumes that object's VM analysis; it does not analyze again.
/// </summary>
public sealed class CompileReturnsCompiledDomainTests {
    private const string Spotless = """
        domain Spotless
        Item: entity {
          Name: Text
          Open: stage { }
        }
        """;

    [Test]
    public async Task Compile_ReturnsCompiledDomain_PrintIsItsConsumer() {
        var (domain, analysis, session) = SliceCProducerLoopCatalogTests.Evolve(Spotless);

        var compiled = session.Compile(domain, analysis);
        var module = session.Lower(domain, analysis);
        var again = session.Compile(domain, analysis);

        await Assert.That(compiled.Domain).IsSameReferenceAs(domain);
        await Assert.That(compiled.DomainAnalysis).IsSameReferenceAs(analysis);
        await Assert.That(compiled.Module).IsSameReferenceAs(module);
        await Assert.That(again).IsSameReferenceAs(compiled);
        await Assert.That(again.VmAnalysis).IsSameReferenceAs(compiled.VmAnalysis);

        var files = session.Emit(domain, analysis);
        var written = FilesFrom(session, compiled);

        await Assert.That(files.Count).IsGreaterThan(0);
        await Assert.That(files.Count).IsEqualTo(written.Count);
        for (var i = 0; i < files.Count; i++) {
            await Assert.That(files[i].FileName).IsEqualTo(written[i].FileName);
            await Assert.That(files[i].Source).IsEqualTo(written[i].Source);
        }

        await Assert.That(session.VmAnalysisCount).IsEqualTo(1);
    }

    /// <summary>
    /// The same split Emit prints: entity trees in catalog order, then scaffolding.
    /// The domain has no entity named Program, so the writer output is the file text.
    /// </summary>
    private static List<(string FileName, string Source)> FilesFrom(DomainSession session, CompiledDomain compiled) {
        var writer = new CSharpWriter(compiled.VmAnalysis);
        var files = new List<(string FileName, string Source)>();
        foreach (var tree in session.ArtifactCatalog.Artifacts.Where(a => a.Descriptor.Id.Type == "entity")) {
            var entityName = tree.Descriptor.Id.Segments[^1];
            var types = (IReadOnlyList<TypeDefinitionNode>)tree.Payload!;
            files.Add(($"{entityName}.cs", writer.Write(types)));
        }

        var scaffolding = session.ArtifactCatalog.Find(ArtifactId.Create([compiled.Domain.Name], "scaffolding"))!;
        var scaffoldingTypes = (IReadOnlyList<TypeDefinitionNode>)scaffolding.Payload!;
        files.Add(("Poly.Types.cs", writer.Write(scaffoldingTypes)));
        return files;
    }
}