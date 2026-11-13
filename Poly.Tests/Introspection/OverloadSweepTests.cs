using System.Text;

using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Libraries.Http;
using Poly.DomainModeling.Ontology;
using Poly.Interpretation;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Packs.Sqlite;
using Poly.Tests.Docs;
using Poly.Tests.Interpretation;

namespace Poly.Tests.Introspection;

/// <summary>
/// T-16 amendment 1: which overload every call site binds, for every shipped document and for the C# reader
/// corpus. The instrument is <see cref="CallSiteCatalogMetadata"/>: one identity
/// (<c>Ns.Type.Method(ParamType1,ParamType2)</c>) per Invoke, New and getter site. The emitted C# cannot show a
/// winner change, because the C# compiler rebinds. <c>OverloadSweepBaseline.txt</c> was captured from origin/master
/// before any production edit; a difference is a defect or a row in the PR, never a new baseline.
/// Set <c>POLY_SWEEP_OUT</c> to a path to write the current sweep there instead of comparing.
/// </summary>
public class OverloadSweepTests {
    private static readonly ExtensionCatalog Catalog = ExtensionCatalog.Core
        .With(new SqliteLibrary())
        .With(new HttpLibrary());

    private static readonly string[] Seed = [.. ExtensionCatalog.ProductAuthoring, "sqlite"];

    private static readonly string[] SkippedDirectories = ["bin", "obj", ".git", ".t3", "node_modules", ".vs", "artifacts"];

    private static string BaselinePath() =>
        Path.Combine(GoldenDomains.RepoRoot, "Poly.Tests", "Introspection", "OverloadSweepBaseline.txt");

    [Test]
    public async Task EveryShippedDocumentAndTheCSharpCorpus_BindTheSameOverloads_AsTheBaseline() {
        var sweep = Run();
        if (Environment.GetEnvironmentVariable("POLY_SWEEP_OUT") is { Length: > 0 } output) {
            await File.WriteAllTextAsync(output, sweep);
            return;
        }
        var baseline = (await File.ReadAllTextAsync(BaselinePath())).Replace("\r\n", "\n");
        await Assert.That(sweep).IsEqualTo(baseline);
    }

    [Test]
    public async Task Sweep_CoversEnoughSites_ToBeAnInstrument() {
        var sweep = Run();
        var identities = sweep.Split('\n').Count(static line => line.StartsWith("  ", StringComparison.Ordinal));
        var documents = sweep.Split('\n').Count(static line => line.StartsWith("## poly ", StringComparison.Ordinal));
        var units = sweep.Split('\n').Count(static line => line.StartsWith("## csharp ", StringComparison.Ordinal));
        await Assert.That(documents).IsGreaterThan(40);
        await Assert.That(units).IsGreaterThan(20);
        await Assert.That(identities).IsGreaterThan(200);
    }

    private static string Run() {
        var text = new StringBuilder();
        foreach (var (name, source) in PolyDocuments())
            text.Append(SweepPoly(name, source));
        var index = 0;
        foreach (var (source, unit) in NodeFactCorpus.Units)
            text.Append(SweepCSharp(index++, source, unit));
        return text.ToString();
    }

    private static IEnumerable<(string Name, string Source)> PolyDocuments() {
        var root = GoldenDomains.RepoRoot;
        var files = Directory.EnumerateFiles(root, "*.poly", SearchOption.AllDirectories)
            .Where(path => !Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar)
                .Any(segment => SkippedDirectories.Contains(segment, StringComparer.Ordinal)))
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal);
        foreach (var relative in files)
            yield return ($"file {relative}", File.ReadAllText(Path.Combine(root, relative)));

        foreach (var (id, wrapper) in GuideBlocks.Wrappers().OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            yield return ($"wrapper {id}", GuideBlocks.Splice(wrapper, [""]).Text);

        var guide = GuideBlocks.Load();
        foreach (var block in guide.Blocks) {
            string composed;
            try {
                composed = (block.Marker is null ? GuideBlocks.Probe(block) : GuideBlocks.Compose(block, guide.Wrappers)).Text;
            }
            catch (InvalidOperationException) {
                composed = GuideBlocks.Probe(block).Text;
            }
            yield return ($"guide-line {block.Line}", composed);
        }
    }

    private static string SweepPoly(string name, string source) {
        var text = new StringBuilder();
        text.Append("## poly ").Append(name).Append('\n');
        try {
            var session = DomainSession.ForSource(source, Seed, Catalog);
            var changes = DomainCompilation.WithSeed(new PolyDslParser(source, session).Parse(), Seed);
            var outcome = new DomainEvolution(new Domain("_", [])).ApplyAndAnalyze(changes, session: session);
            if (!outcome.Succeeded) {
                var codes = outcome.Analysis.Diagnostics
                    .Where(static d => d.Severity == DiagnosticSeverity.Error)
                    .Select(static d => d.Code)
                    .Order(StringComparer.Ordinal);
                text.Append("  status: analysis-errors ").Append(string.Join(',', codes)).Append('\n');
                return text.ToString();
            }
            var compiled = session.Compile(outcome.Root, outcome.Analysis);
            AppendIdentities(text, compiled.VmAnalysis);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) {
            text.Append("  status: exception ").Append(ex.GetType().Name).Append('\n');
        }
        return text.ToString();
    }

    private static string SweepCSharp(int index, string source, Node unit) {
        var firstLine = source.Split('\n').Select(static l => l.Trim()).FirstOrDefault(static l => l.Length > 0) ?? "";
        var text = new StringBuilder();
        text.Append("## csharp ").Append(index).Append(' ').Append(firstLine.Length > 60 ? firstLine[..60] : firstLine).Append('\n');
        try {
            AppendIdentities(text, Interpreter.Analyze(unit));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) {
            text.Append("  status: exception ").Append(ex.GetType().Name).Append('\n');
        }
        return text.ToString();
    }

    private static void AppendIdentities(StringBuilder text, AnalysisResult analysis) {
        var errors = analysis.Diagnostics.Count(static d => d.Severity == DiagnosticSeverity.Error);
        text.Append("  errors: ").Append(errors).Append('\n');
        foreach (var site in analysis.GetCallSiteCatalog() ?? [])
            text.Append("  ").Append(site.Identity).Append('\n');
    }
}