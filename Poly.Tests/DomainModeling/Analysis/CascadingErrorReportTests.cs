using System.Text.RegularExpressions;

using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;
using Poly.DslCompiler;
using Poly.Packs.Sqlite;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// Report only: breaks each sample domain one way at a time and counts the Error
/// diagnostics that one mistake produces. One edit is one root cause. An error that names
/// the broken element is the direct report of it; an error that does not is counted as
/// cascade. That is a name match on the message, not proof of cause.
/// Nothing here fails on the counts.
/// Run it with: dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter "/*/*/CascadingErrorReportTests/*" --output Detailed
/// </summary>
public sealed class CascadingErrorReportTests {
    private static readonly ExtensionCatalog Catalog = ExtensionCatalog.Core
        .With(new SqliteLibrary())
        .With(new HttpLibrary());

    private static readonly string[] Seed = [.. ExtensionCatalog.ProductAuthoring, "sqlite"];

    // The live probes and the demo, minus the probe that is invalid by design.
    private static readonly string[] SampleRoots = ["docs/probes", "demo/live"];
    private static readonly string[] InvalidProbes = ["nested-invoke-type-mismatch.poly"];

    // Each break edits the DSL text in one place and returns the edited text and the name
    // it broke, or null when the domain has no element of that kind that something else
    // refers to.
    private sealed record Break(string Name, Func<string, (string Text, string Victim)?> Apply);

    private static readonly Break[] Breaks = [
        new("rename an entity", text => RenameDeclaration(text, @"^(\w+): entity\b")),
        new("rename a scalar property", text => RenameDeclaration(text, @"^\s+(\w+): (?:Text|Number|Boolean|Date)\b")),
        new("rename a stage", text => RenameDeclaration(text, @"^\s+(\w+): stage\b")),
        new("Number property becomes Text", ChangeNumberToText),
        new("delete an action", DeleteAction),
    ];

    [Test]
    public async Task SampleDomains_FindsTheSamples() =>
        await Assert.That(SampleDomains().Count()).IsGreaterThanOrEqualTo(10);

    [Test]
    public async Task ChangeNumberToText_EditsTheTypeNotTheName() {
        var broken = ChangeNumberToText("Phone: entity {\n  PhoneNumber: Number\n}\n");
        await Assert.That(broken!.Value.Text).IsEqualTo("Phone: entity {\n  PhoneNumber: Text\n}\n");
        await Assert.That(broken.Value.Victim).IsEqualTo("PhoneNumber");
    }

    [Test]
    public async Task IsReferencedElsewhere_IgnoresSecondDeclarationsStringsAndComments() {
        await Assert.That(IsReferencedElsewhere("A: entity {\n  X: stage { }\n  go: action { transition to X }\n}", "X")).IsTrue();
        await Assert.That(IsReferencedElsewhere("A: entity {\n  X: stage { }\n}\nB: entity {\n  X: stage { }\n}", "X")).IsFalse();
        await Assert.That(IsReferencedElsewhere("A: entity {\n  X: stage { }\n  P: Text default(\"X\")\n}", "X")).IsFalse();
        await Assert.That(IsReferencedElsewhere("A: entity {\n  X: stage { } // X again\n}", "X")).IsFalse();
    }

    [Test]
    public void ReportErrorsPerBreak() {
        var domains = SampleDomains().ToList();
        var results = domains.ToDictionary(
            path => path,
            path => {
                var poly = File.ReadAllText(Path.Combine(FindRepoRoot(), path));
                return Breaks.Select(b => Measure(poly, b)).ToList();
            });

        Console.WriteLine("Error diagnostics per single break (report only)");
        WriteTable(
            ["domain", .. Breaks.Select(b => b.Name)],
            domains.Select(path => (string[])[path, .. results[path].Select(m => m.Cell)]));

        Console.WriteLine();
        Console.WriteLine("cell: Error diagnostics for the break (of which name the broken element); parser: the parser stopped at the first bad reference (counted as 1 error); n/a: no element of that kind that something else refers to; load failed: any other exception");
        Console.WriteLine();
        WriteTable(
            ["break", "applied", "parser stopped", "n/a", "load failed", "no Error", "most Errors", "total Errors",
                "name the broken element", "cascade (do not name it)", "breaks with cascade", "most cascade in one break"],
            Breaks.Select((b, i) => {
                var column = results.Values.Select(r => r[i]).ToList();
                var applied = column.Where(m => m.Errors is not null).ToList();
                return (string[])[
                    b.Name,
                    applied.Count.ToString(),
                    applied.Count(m => m.Kind == "parser").ToString(),
                    column.Count(m => m.Kind == "n/a").ToString(),
                    column.Count(m => m.Kind == "load failed").ToString(),
                    applied.Count(m => m.Errors == 0).ToString(),
                    applied.Select(m => m.Errors!.Value).DefaultIfEmpty(0).Max().ToString(),
                    applied.Sum(m => m.Errors!.Value).ToString(),
                    applied.Sum(m => m.Named).ToString(),
                    applied.Sum(m => m.Errors!.Value - m.Named).ToString(),
                    applied.Count(m => m.Errors > m.Named).ToString(),
                    applied.Select(m => m.Errors!.Value - m.Named).DefaultIfEmpty(0).Max().ToString(),
                ];
            }));
    }

    private static void WriteTable(string[] header, IEnumerable<string[]> rows) {
        Console.WriteLine("| " + string.Join(" | ", header) + " |");
        Console.WriteLine("|" + string.Join("|", header.Select(_ => "---")) + "|");
        foreach (var row in rows)
            Console.WriteLine("| " + string.Join(" | ", row) + " |");
    }

    private sealed record Measurement(string Kind, int? Errors, int Named) {
        public string Cell => Errors is null ? Kind : Kind == "parser" ? $"parser ({Named})" : $"{Errors} ({Named})";
    }

    private static Measurement Measure(string poly, Break kind) {
        var broken = kind.Apply(poly);
        if (broken is null)
            return new Measurement("n/a", null, 0);
        try {
            var session = DomainSession.ForSource(broken.Value.Text, Seed, Catalog);
            var changes = DomainCompilation.WithSeed(new PolyDslParser(broken.Value.Text, session).Parse(), Seed);
            var outcome = new DomainEvolution(new Domain("_", [])).Apply(changes, session: session);
            var errors = outcome.Analysis.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            var named = errors.Count(d => Mentions(d.Message, broken.Value.Victim));
            return new Measurement("measured", errors.Count, named);
        }
        catch (FormatException ex) {
            // The parser reports the first bad reference and stops, so this is one error.
            return new Measurement("parser", 1, Mentions(ex.Message, broken.Value.Victim) ? 1 : 0);
        }
        catch (Exception) {
            return new Measurement("load failed", null, 0);
        }
    }

    // A message names the broken element by its old name, or by the new name a rename gave it.
    private static bool Mentions(string message, string name) =>
        Regex.IsMatch(message, $@"\b{Regex.Escape(name)}(?:Gone)?\b");

    // Renames the first declaration that matches and that something else refers to, so the
    // other mentions of the name dangle.
    private static (string Text, string Victim)? RenameDeclaration(string text, string pattern) {
        foreach (Match m in Regex.Matches(text, pattern, RegexOptions.Multiline)) {
            var name = m.Groups[1];
            if (IsReferencedElsewhere(text, name.Value))
                return (text.Insert(name.Index + name.Length, "Gone"), name.Value);
        }
        return null;
    }

    private static (string Text, string Victim)? ChangeNumberToText(string text) {
        var m = Regex.Match(text, @"^\s+(\w+): (Number)\b", RegexOptions.Multiline);
        if (!m.Success)
            return null;
        var type = m.Groups[2];
        return (text.Remove(type.Index, type.Length).Insert(type.Index, "Text"), m.Groups[1].Value);
    }

    // Deletes the first action declaration (header through its closing brace) that something else refers to.
    private static (string Text, string Victim)? DeleteAction(string text) {
        foreach (Match m in Regex.Matches(text, @"^\s+(\w+): action\b", RegexOptions.Multiline)) {
            var name = m.Groups[1].Value;
            if (!IsReferencedElsewhere(text, name))
                continue;
            var open = text.IndexOf('{', m.Index);
            if (open < 0)
                continue;
            var depth = 0;
            for (var i = open; i < text.Length; i++) {
                if (text[i] == '{') depth++;
                else if (text[i] == '}' && --depth == 0)
                    return (text.Remove(m.Index, i + 1 - m.Index), name);
            }
        }
        return null;
    }

    // True when the name is declared once and mentioned somewhere else in code. Strings and
    // comments do not count, and a name declared more than once is skipped because a
    // mention cannot be tied to one of the declarations.
    private static bool IsReferencedElsewhere(string text, string name) {
        var code = Regex.Replace(text, "\"(?:[^\"\\\\]|\\\\.)*\"|//[^\n]*", "");
        var escaped = Regex.Escape(name);
        var declarations = Regex.Matches(code, $@"^\s*{escaped}\s*:", RegexOptions.Multiline).Count;
        var mentions = Regex.Matches(code, $@"\b{escaped}\b").Count;
        return declarations == 1 && mentions > 1;
    }

    private static IEnumerable<string> SampleDomains() {
        var root = FindRepoRoot();
        return SampleRoots
            .Select(r => Path.Combine(root, r))
            .Where(Directory.Exists)
            .SelectMany(dir => Directory.EnumerateFiles(dir, "*.poly", SearchOption.AllDirectories))
            .Where(path => !InvalidProbes.Contains(Path.GetFileName(path)))
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal);
    }

    private static string FindRepoRoot() {
        var dir = AppContext.BaseDirectory;
        while (dir is not null) {
            if (File.Exists(Path.Combine(dir, "docs/CORE.md")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new InvalidOperationException("Could not find repo root from " + AppContext.BaseDirectory);
    }
}
