using System.Reflection;
using System.Runtime.Loader;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Libraries.Tests;
using Poly.DomainModeling.Ontology;

using Artifact = Poly.DomainModeling.Compile.Artifact;

namespace Poly.Tests.DomainModeling.Compile;

/// <summary>
/// #374 slice S3: <c>uses tests</c> registers an artifact contributor that adds <c>test-tree</c> and
/// <c>test-file</c> artifacts to the Emit catalog, catalog-only, deterministic, and fail-closed.
/// The generated suite binds the real generated CLR (compiled here with Roslyn), never a second evaluator.
/// </summary>
public sealed class TestsArtifactContributorTests {
    private const string Shop = """
        domain Shop
        uses tests
        Order: entity {
          Reference: Text
          Draft: stage {
            Submit: action { transition to Submitted }
          }
          Submitted: stage {
            Ship: action { transition to Shipped }
          }
          Shipped: stage { }
        }
        """;

    private const string Unsupported = """
        domain Gauge
        uses tests
        Reading: entity {
          Level: Number range(0, 100)
          Open: stage { }
        }
        """;

    private const string NoDoor = """
        domain Plain
        Item: entity {
          Name: Text
          Open: stage { }
        }
        """;

    // Smallest TUnit surface the generated suite uses.
    private const string TUnitStub = """
        using System;
        using System.Threading.Tasks;
        public sealed class TestAttribute : Attribute { }
        public static class Assert {
            public static Check<T> That<T>(T actual) => new Check<T>(actual);
        }
        public sealed class Check<T> {
            private readonly T _actual;
            public Check(T actual) { _actual = actual; }
            public Task IsTrue() { if (!(_actual is bool b && b)) throw new Exception("expected true"); return Task.CompletedTask; }
            public Task IsFalse() { if (!(_actual is bool b && !b)) throw new Exception("expected false"); return Task.CompletedTask; }
            public Task IsEqualTo(T expected) { if (!Equals(_actual, expected)) throw new Exception($"expected {expected} got {_actual}"); return Task.CompletedTask; }
            public Task Contains(string part) { if (!(_actual is string s && s.Contains(part))) throw new Exception($"expected '{_actual}' to contain '{part}'"); return Task.CompletedTask; }
        }
        """;

    [Test]
    public async Task Emit_UsesTests_CatalogDeclaresAndReportsTestArtifacts() {
        var (domain, analysis, session) = Evolve(Shop);

        session.Emit(domain, analysis);

        var catalog = session.ArtifactCatalog;
        var file = catalog.Find(ArtifactId.Create(["Shop", "Order"], TestFile.Type));
        var tree = catalog.Find(ArtifactId.Create(["Shop", "Order"], TestTree.Type));
        await Assert.That(file).IsNotNull();
        await Assert.That(tree).IsNotNull();
        await Assert.That(file!.Descriptor.Producer).IsEqualTo(nameof(TestsArtifactContributor));
        await Assert.That(file.Descriptor.References.Select(r => r.ToString()).Order(StringComparer.Ordinal))
            .IsEquivalentTo(["Shop/Order#entity", "Shop/Order#test-tree"]);
        await Assert.That(catalog.ToText()).Contains("test-file|Shop/Order|TestsArtifactContributor");
        await Assert.That(catalog.FindDanglingOrWrongType()).IsEmpty();
    }

    [Test]
    public async Task Emit_UsesTests_DoesNotAppendTestsToTheFileList() {
        var (domain, analysis, session) = Evolve(Shop);

        var files = session.Emit(domain, analysis);

        await Assert.That(files.Select(f => f.FileName).SequenceEqual(["Order.cs", "Poly.Types.cs"])).IsTrue();
    }

    [Test]
    public async Task Emit_WithoutUsesTests_RegistersNoTestArtifacts() {
        var (domain, analysis, session) = Evolve(NoDoor);

        session.Emit(domain, analysis);

        await Assert.That(session.ArtifactCatalog.Artifacts.Any(a => a.Descriptor.Id.Type.StartsWith("test-", StringComparison.Ordinal)))
            .IsFalse();
    }

    [Test]
    public async Task Emit_Twice_OnFreshSessions_IsByteIdentical() {
        var first = TestText(Shop);
        var second = TestText(Shop);

        await Assert.That(first).Contains("Ship_FromDraft_IsRefused");
        await Assert.That(second).IsEqualTo(first);
    }

    [Test]
    public async Task GeneratedSuite_AgainstGeneratedCode_AllPass() {
        var (ran, failures) = RunGeneratedSuite(Shop, mutate: null);

        await Assert.That(ran).IsEqualTo(2);
        await Assert.That(failures).IsEmpty();
    }

    [Test]
    public async Task GeneratedSuite_AgainstMutatedGuard_GoesRed() {
        // Drop the stage guard of Ship: the generated suite must notice.
        var (ran, failures) = RunGeneratedSuite(Shop, mutate: src => src.Replace(
            "this.CurrentStage != OrderStage.Submitted", "false"));

        await Assert.That(ran).IsEqualTo(2);
        await Assert.That(failures.Any(f => f.StartsWith("Ship_", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task Emit_EntityTheProducerCannotHandle_IsDeclaredUnsupported_NotAnEmptyFile() {
        var (domain, analysis, session) = Evolve(Unsupported);

        session.Emit(domain, analysis);

        var catalog = session.ArtifactCatalog;
        await Assert.That(catalog.Find(ArtifactId.Create(["Gauge", "Reading"], TestFile.Type))).IsNull();
        var outcome = catalog.Find(ArtifactId.Create(["Gauge", "Reading"], TestUnsupported.Type));
        await Assert.That(outcome).IsNotNull();
        var reason = TestUnsupported.Reason(outcome!);
        await Assert.That(reason).Contains("Reading.Level");
        await Assert.That(catalog.FindDanglingOrWrongType()).IsEmpty();
    }

    private static string TestText(string poly) {
        var (domain, analysis, session) = Evolve(poly);
        session.Emit(domain, analysis);
        return string.Join(
            "\n---\n",
            session.ArtifactCatalog.Artifacts
                .Where(a => a.Descriptor.Id.Type == TestFile.Type)
                .OrderBy(a => a.Descriptor.Id.ToString(), StringComparer.Ordinal)
                .Select(TestFile.Text))
            + "\n===\n" + session.ArtifactCatalog.ToText();
    }

    /// <summary>Compiles the Emit output plus the generated suite plus a TUnit stub and runs every test method; returns failed method names.</summary>
    private static (int Ran, List<string> Failures) RunGeneratedSuite(string poly, Func<string, string>? mutate) {
        var (domain, analysis, session) = Evolve(poly);
        var files = session.Emit(domain, analysis);
        var sources = files.Select(f => (f.FileName, Source: mutate is null ? f.Source : mutate(f.Source))).ToList();
        foreach (var artifact in session.ArtifactCatalog.Artifacts.Where(a => a.Descriptor.Id.Type == TestFile.Type))
            sources.Add(($"{artifact.Descriptor.Id.Segments[^1]}.Tests.cs", TestFile.Text(artifact)));
        sources.Add(("TUnitStub.cs", TUnitStub));
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))!
            .Split(Path.PathSeparator)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p));
        var compilation = CSharpCompilation.Create(
            "GeneratedSuite_" + Guid.NewGuid().ToString("N"),
            sources.Select(s => CSharpSyntaxTree.ParseText(s.Source, path: s.FileName)),
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var pe = new MemoryStream();
        var emit = compilation.Emit(pe);
        if (!emit.Success)
            throw new InvalidOperationException(
                string.Join("\n", emit.Diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error))
                + "\n" + string.Join("\n", sources.Select(s => s.Source)));
        pe.Position = 0;
        var assembly = new AssemblyLoadContext(compilation.AssemblyName, isCollectible: true).LoadFromStream(pe);
        var failed = new List<string>();
        var ran = 0;
        var testAttribute = assembly.GetType("TestAttribute")!;
        foreach (var type in assembly.GetTypes().OrderBy(t => t.FullName, StringComparer.Ordinal)) {
            foreach (var method in type.GetMethods().Where(m => m.GetCustomAttributes(testAttribute, false).Length > 0)
                .OrderBy(m => m.Name, StringComparer.Ordinal)) {
                ran++;
                try {
                    var instance = Activator.CreateInstance(type);
                    ((Task)method.Invoke(instance, null)!).GetAwaiter().GetResult();
                }
                catch (Exception ex) {
                    failed.Add($"{method.Name}: {(ex.InnerException ?? ex).Message}");
                }
            }
        }
        return (ran, failed);
    }

    private static (Domain Domain, AnalysisResult Analysis, DomainSession Session) Evolve(string poly) {
        var session = DomainSession.ForSource(poly, ExtensionCatalog.ProductAuthoring, ExtensionCatalog.Core);
        var result = new DomainEvolution(new Domain("_", [])).ApplyAndAnalyze(
            new PolyDslParser(poly, session).Parse(), session: session);
        if (!result.Succeeded)
            throw new InvalidOperationException(result.FailureSummary ?? "evolution rejected the domain");
        return (result.Root, result.Analysis, session);
    }
}