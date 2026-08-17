using Poly.Analysis;
using Poly.DomainModeling;
using Poly.DomainModeling.Packs;

using CompileMode = Poly.DslCompiler.CompileMode;
using Compiler = Poly.DslCompiler.DslCompiler;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// The artifact hook: packs may emit files from the analyzed domain via
/// <see cref="IArtifactContributor"/>. The compiler asks contributors after analysis
/// succeeds; structural analysis failures still fail closed first and contributors
/// are never asked.
/// </summary>
public class DslCompilerArtifactContributorTests {
    private const string SampleDomain = """
        domain Library

        Book: entity {
          Title: Text required
          Pages: Number
        }
        """;

    private const string StructurallyInvalidDomain = """
        domain Library

        Book: entity {
          Title: Text
        }

        Book: entity {
          Pages: Number
        }
        """;

    private sealed class HelloContributor : IArtifactContributor {
        public bool Called { get; private set; }

        public IReadOnlyList<(string FileName, string Source)> Contribute(
            Domain domain, AnalysisResult analysis) {
            Called = true;
            return [("hello.txt", $"hello from {domain.Name}")];
        }
    }

    [Test]
    public async Task Compile_All_WithArtifactContributor_IncludesContributedFile() {
        var contributor = new HelloContributor();
        var result = new Compiler()
            .AddArtifactContributor(contributor)
            .Compile(SampleDomain, CompileMode.All);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Files).IsNotNull();

        // Core entity C# export stays in core — not moved into a pack.
        await Assert.That(result.Files!.Any(f => f.FileName == "Book.cs")).IsTrue();

        var hello = result.Files!.Single(f => f.FileName == "hello.txt");
        await Assert.That(hello.Source).IsEqualTo("hello from Library");
        await Assert.That(contributor.Called).IsTrue();
    }

    [Test]
    public async Task Compile_LibraryRegisteringArtifact_IncludesContributedFile() {
        var contributor = new HelloContributor();
        var result = new Compiler().Compile(
            SampleDomain,
            CompileMode.Entities,
            new HelloLibrary(contributor));

        await Assert.That(result.Success).IsTrue();
        var hello = result.Files!.Single(f => f.FileName == "hello.txt");
        await Assert.That(hello.Source).IsEqualTo("hello from Library");
        await Assert.That(contributor.Called).IsTrue();
    }

    [Test]
    public async Task Compile_WithArtifactContributor_OnStructuralFailure_EmitsNothing() {
        var contributor = new HelloContributor();
        var result = new Compiler()
            .AddArtifactContributor(contributor)
            .Compile(StructurallyInvalidDomain, CompileMode.All);

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.Files).IsNull();
        await Assert.That(contributor.Called).IsFalse();
    }

    private sealed class HelloLibrary(HelloContributor contributor) : IDomainLibrary {
        public string Id => "hello-artifact";

        public void Register(DomainHostBuilder builder) =>
            builder.AddArtifactContributor(contributor);
    }
}