using System.Text.RegularExpressions;

using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;
using Poly.DslCompiler;
using Poly.Packs.Sqlite;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// Report only: breaks each sample domain one way at a time and counts how many
/// Error diagnostics that one mistake produces. One edit is one root cause, so any
/// count above 1 is cascade. Nothing here fails on the counts.
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

    // Each break edits the DSL text in one place and returns the edited text and the
    // name it broke, or null when the domain has nothing of that kind to break.
    private sealed record Break(string Name, Func<string, (string Text, string Victim)?> Apply);

    private static readonly Break[] Breaks = [
        new("rename an entity", text => RenameDeclaration(text, @"^(\w+): entity\b", usedElsewhere: false)),
        new("rename a property", text => RenameDeclaration(text, @"^\s+(\w+): (?:Text|Number|Boolean|Date)\b", usedElsewhere: true)),
        new("rename a stage", text => RenameDeclaration(text, @"^\s+(\w+): stage\b", usedElsewhere: true)),
        new("Number property becomes Text", ChangeNumberToText),
        new("delete an action", DeleteAction),
    ];

    [Test]
    public async Task SampleDomains_FindsTheSamples() =>
        await Assert.That(SampleDomains().Count()).IsGreaterThanOrEqualTo(10);

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
        Console.WriteLine("| domain | " + string.Join(" | ", Breaks.Select(b => b.Name)) + " |");
        Console.WriteLine("|---|" + string.Join("|", Breaks.Select(_ => "---")) + "|");
        foreach (var path in domains)
            Console.WriteLine($"| {path} | " + string.Join(" | ", results[path].Select(m => m.Cell)) + " |");

        Console.WriteLine();
        Console.WriteLine("cell: Error diagnostics for the break (of which name the broken element); parser: the parser stopped at the first bad reference (counted as 1 error); n/a: nothing to break; load failed: any other exception");
        Console.WriteLine();
        Console.WriteLine("| break | applied | of which parser stopped | n/a | load failed | no Error | one Error | more than one | most | total Errors | name the broken element |");
        Console.WriteLine("|---|---|---|---|---|---|---|---|---|---|---|");
        for (var i = 0; i < Breaks.Length; i++) {
            var column = results.Values.Select(r => r[i]).ToList();
            var applied = column.Where(m => m.Errors is not null).ToList();
            Console.WriteLine(
                $"| {Breaks[i].Name} | {applied.Count} | {applied.Count(m => m.Kind == "parser")} | {column.Count(m => m.Kind == "n/a")} | {column.Count(m => m.Kind == "load failed")} "
                + $"| {applied.Count(m => m.Errors == 0)} | {applied.Count(m => m.Errors == 1)} | {applied.Count(m => m.Errors > 1)} "
                + $"| {applied.Select(m => m.Errors!.Value).DefaultIfEmpty(0).Max()} | {applied.Sum(m => m.Errors!.Value)} | {applied.Sum(m => m.Named)} |");
        }
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

    private static bool Mentions(string message, string name) =>
        Regex.IsMatch(message, $@"\b{Regex.Escape(name)}\b");

    // Renames the first declaration that matches, so every other mention of the name dangles.
    // With usedElsewhere, skips declarations nothing else mentions.
    private static (string Text, string Victim)? RenameDeclaration(string text, string pattern, bool usedElsewhere) {
        foreach (Match m in Regex.Matches(text, pattern, RegexOptions.Multiline)) {
            var name = m.Groups[1];
            if (usedElsewhere && Regex.Matches(text, $@"\b{Regex.Escape(name.Value)}\b").Count < 2)
                continue;
            return (text.Insert(name.Index + name.Length, "Gone"), name.Value);
        }
        return null;
    }

    private static (string Text, string Victim)? ChangeNumberToText(string text) {
        var m = Regex.Match(text, @"^\s+(\w+): Number\b", RegexOptions.Multiline);
        if (!m.Success)
            return null;
        var number = text.IndexOf("Number", m.Index, StringComparison.Ordinal);
        return (text.Remove(number, "Number".Length).Insert(number, "Text"), m.Groups[1].Value);
    }

    // Deletes the first action declaration (header through its closing brace) that something else mentions.
    private static (string Text, string Victim)? DeleteAction(string text) {
        foreach (Match m in Regex.Matches(text, @"^\s+(\w+): action\b", RegexOptions.Multiline)) {
            var name = m.Groups[1].Value;
            if (Regex.Matches(text, $@"\b{Regex.Escape(name)}\b").Count < 2)
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
            if (File.Exists(Path.Combine(dir, "Poly.sln"))
                || File.Exists(Path.Combine(dir, "docs/CORE.md")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new InvalidOperationException("Could not find repo root from " + AppContext.BaseDirectory);
    }
}
