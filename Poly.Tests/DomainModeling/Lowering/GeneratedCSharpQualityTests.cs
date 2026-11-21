using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Libraries.Http;
using Poly.DomainModeling.Ontology;
using Poly.DslCompiler;
using Poly.Packs.Sqlite;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// #546, generated C# quality: the issue's reported shapes, re-checked against the writer with the
/// issue's own domain shape. The required guard is <c>== null</c>, so the length check after it
/// reaches the empty string; an assign of a required patterned value has no dead <c>!= null</c>
/// conjunct (it used to leave the value maybe-null: CS8601); no exception type is generated (the
/// module imports the shipped one); and the private parameterless constructor exists only for the
/// persistence pack, which materializes rows with it.
/// </summary>
public sealed class GeneratedCSharpQualityTests {
    private static readonly ExtensionCatalog Catalog = ExtensionCatalog.Core
        .With(new SqliteLibrary())
        .With(new HttpLibrary());

    private static readonly string[] Seed = [.. ExtensionCatalog.ProductAuthoring, "sqlite"];

    // The issue's shapes: required Text with a length lower bound, an email pattern, and the same
    // property assigned by an action (create and assign both print a pattern guard).
    private const string Tracker = """
        domain Tracker

        User: entity {
          Name: Text length(1, 80)
          Email: Text pattern("^[a-z]+@[a-z]+$")
          Code: Text pattern("^[A-Z]{3}$")
          Rename: action (name: Text) { assign Name to name }
          Relabel: action (code: Text) { assign Code to code }
        }

        Issue: entity {
          Title: Text length(1, 200)
          Body: Text length(1, 4000)
          Open: action (title: Text) { assign Title to title }
        }
        """;

    // ── the required guard leaves the length check reachable ─────────────────────────────────────────

    [Test]
    public async Task Export_RequiredText_ChecksTheEmptyStringAfterTheNullGuard() {
        var files = Export(Tracker);
        var issue = files.Single(f => f.FileName == "Issue.cs").Source;

        // The reported "dead" pair: a null-or-empty required guard would make `Length < 1L` impossible.
        await Assert.That(issue).DoesNotContain("IsNullOrEmpty");
        await Assert.That(issue).Contains("if (title == null)");
        await Assert.That(issue).Contains("if (__title.Length < 1L)");
    }

    [Test]
    public async Task Export_RequiredText_LengthLowerBoundRejectsTheEmptyString() {
        var assembly = ExportedCSharp.CompileAndLoad(Export(Tracker));
        var type = assembly.GetType("Tracker.Issue")!;

        // The check is reachable: "" passes the null guard and fails the length bound.
        var empty = ExportedCSharp.InvokeCreate(type, ("title", ""), ("body", "b"));
        await Assert.That(IsSuccess(empty)).IsFalse();
        await Assert.That(ErrorMessage(empty)).Contains("'Title' must be at least 1 characters.");
        var filled = ExportedCSharp.InvokeCreate(type, ("title", "t"), ("body", "b"));
        await Assert.That(IsSuccess(filled)).IsTrue();
    }

    // ── an assign of a required patterned value has no dead null conjunct ────────────────────────────

    [Test]
    public async Task Export_AssignRequiredPattern_HasNoDeadNullConjunct() {
        var files = Export(Tracker);
        var user = files.Single(f => f.FileName == "User.cs").Source;

        await Assert.That(user).Contains(
            "if (!System.Text.RegularExpressions.Regex.IsMatch(assignValue0, \"^[A-Z]{3}\\\\z\"))");
        await Assert.That(user).DoesNotContain("assignValue0 != null &&");
    }

    [Test]
    public async Task Export_AssignRequiredPattern_CompilesWithWarningsAsErrors() {
        // The dead conjunct left the assigned value maybe-null: CS8601 in a strict build.
        AssertCompiles(Export(Tracker));
    }

    // ── no generated exception type ─────────────────────────────────────────────────────────────────

    [Test]
    public async Task Export_GeneratesNoExceptionType() {
        var files = Export(Tracker);
        var assembly = ExportedCSharp.CompileAndLoad(files);

        await Assert.That(assembly.GetTypes().Where(t => typeof(Exception).IsAssignableFrom(t))).IsEmpty();
        // The module imports the shipped runtime type instead of declaring its own.
        await Assert.That(files.Single(f => f.FileName == "User.cs").Source).Contains("using Poly.Runtime;");
    }

    // ── the private parameterless constructor, and who uses it ──────────────────────────────────────

    [Test]
    public async Task Export_WithPersistence_EmitsTheMaterializationConstructor() {
        var files = Export("""
            domain Tracker
            uses sqlite

            User: entity {
              Name: Text
            }
            """);
        var user = files.Single(f => f.FileName == "User.cs").Source;

        // EF materializes a row through a parameterless constructor; the persistence pack is what asks for it.
        await Assert.That(user).Contains("private User() { }");
        AssertCompiles(files);
    }

    [Test]
    public async Task Export_WithoutPersistence_EmitsNoParameterlessConstructor() {
        var files = Export("""
            domain Tracker

            User: entity {
              Name: Text
            }
            """, persistence: false);
        var user = files.Single(f => f.FileName == "User.cs").Source;

        await Assert.That(user).DoesNotContain("private User() { }");
        AssertCompiles(files);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────────

    private static IReadOnlyList<(string FileName, string Source)> Export(string poly, bool persistence = true) {
        var catalog = persistence
            ? Catalog
            : ExtensionCatalog.Core.With(new HttpLibrary());
        string[] seed = persistence
            ? Seed
            : [.. ExtensionCatalog.ProductAuthoring];
        var session = DomainSession.ForSource(poly, seed, catalog);
        var changes = DomainCompilation.WithSeed(new PolyDslParser(poly, session).Parse(), seed);
        var outcome = new DomainEvolution(new Domain("_", [])).ApplyAndAnalyze(changes, session: session);
        if (!outcome.Succeeded)
            throw new InvalidOperationException(string.Join("; ", outcome.Analysis.Diagnostics
                .Where(d => d.Severity == Poly.Analysis.DiagnosticSeverity.Error)
                .Select(d => d.Message)));
        return session.Emit(outcome.Root, outcome.Analysis);
    }

    private static void AssertCompiles(IReadOnlyList<(string FileName, string Source)> files) {
        var diagnostics = ExportedCSharp.CompileWithWarningsAsErrors("Quality546", files);
        if (diagnostics.Count == 0)
            return;
        throw new InvalidOperationException(string.Join("\n", diagnostics.Select(d => $"{d.Location.SourceTree?.FilePath}: {d}")));
    }

    private static bool IsSuccess(object result) =>
        (bool)result.GetType().GetProperty("IsSuccess")!.GetValue(result)!;

    private static string? ErrorMessage(object result) =>
        result.GetType().GetProperty("ErrorMessage")?.GetValue(result) as string;
}