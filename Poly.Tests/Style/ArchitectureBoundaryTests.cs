using System.Text;
using System.Text.RegularExpressions;

namespace Poly.Tests.Style;

/// <summary>
/// I8: the layers that state meaning do not name execution, reflection or CLR type facts. <c>Vm/**</c> is the host that
/// compiles to <c>System.Linq.Expressions</c>, and <c>CommonLanguageRuntime/**</c> is the host that reflects and names
/// runtime types; everything that states meaning asks the platform instead.
///
/// Two checks:
/// <list type="bullet">
/// <item>a namespace ban (<c>System.Linq.Expressions</c>, <c>System.Reflection</c>), matched as a substring so a
/// qualified name or an alias is caught;</item>
/// <item>a per-file, per-pattern budget for CLR type facts (<c>typeof(</c>, <c>RuntimeType</c>, <c>GetRuntimeType</c>,
/// <c>ClrTypeReference</c>, <c>Nullable.GetUnderlyingType</c>, and the <c>IsEnum</c>/<c>IsPrimitive</c>/<c>IsValueType</c>
/// predicates). The count must equal the budget exactly, so a reduction must lower the number here in the same change
/// and the budget only ever shrinks. Every entry names its removing issue.</item>
/// </list>
/// The scan strips comments and string literals first, so documentation and messages do not count. Known blind spot,
/// named in #972: bare <c>Type</c> parameters and returns are not matched yet.
/// </summary>
public class ArchitectureBoundaryTests {
    private static readonly string[] ScannedRoots = [
        "Poly/Ast",
        "Poly/Introspection",
        "Poly/Interpretation/Analysis"
    ];

    private static readonly string[] ForbiddenNamespaces = [
        "System.Linq.Expressions",
        "System.Reflection"
    ];

    /// <summary>Type-fact patterns the meaning layers may not add to. Word boundaries, so <c>RuntimeType</c> does not
    /// also match inside <c>GetRuntimeType</c>.</summary>
    private static readonly (string Name, Regex Pattern)[] TypeFacts = [
        ("typeof(", new Regex(@"\btypeof\s*\(", RegexOptions.Compiled)),
        ("GetRuntimeType", new Regex(@"\bGetRuntimeType\b", RegexOptions.Compiled)),
        ("RuntimeType", new Regex(@"\bRuntimeType\b", RegexOptions.Compiled)),
        ("ClrTypeReference", new Regex(@"\bClrTypeReference\b", RegexOptions.Compiled)),
        ("Nullable.GetUnderlyingType", new Regex(@"\bNullable\.GetUnderlyingType\b", RegexOptions.Compiled)),
        ("CLR type predicate", new Regex(@"\.IsEnum\b|\.IsPrimitive\b|\.IsValueType\b", RegexOptions.Compiled))
    ];

    /// <summary>Analysis files that still name a reflection/expression fact, each with the row that removes it.</summary>
    private static readonly Dictionary<string, string> AllowedNamespaces = new(StringComparer.Ordinal) {
        ["Poly/Interpretation/Analysis/ConstantFolding/ConstantFoldingPass.cs"] =
            "invokes a declared conversion at analysis time (#954); removing row: one shared evaluator (I9, #972)",
        ["Poly/Interpretation/Analysis/Semantics/CallSiteCatalogPass.cs"] =
            "reads a delegate's parameter shape for the catalog; removing row: I9 moves the fence to the host (#972)",
        ["Poly/Interpretation/Analysis/Semantics/AwaitPattern.cs"] =
            "reads awaiter members by reflection; removing row: I9 moves the fence to the host (#972)"
    };

    /// <summary>Per-file, per-pattern budget. The measured count must equal the budget: a reduction lowers the number
    /// here in the same change, so the budget only shrinks.</summary>
    private static readonly Dictionary<string, (string RemovingRow, Dictionary<string, int> Max)> Budget = new(StringComparer.Ordinal) {
        ["Poly/Interpretation/Analysis/ConstantFolding/ConstantFoldingPass.cs"] = ("analysis-time invocation and CLR primitives (#954); removing row: I9 (#972)", new(StringComparer.Ordinal) { ["CLR type predicate"] = 1, ["typeof("] = 66 }),
        ["Poly/Interpretation/Analysis/Semantics/AwaitPattern.cs"] = ("awaiter reflection and CLR members; removing row: I9 (#972)", new(StringComparer.Ordinal) { ["GetRuntimeType"] = 1, ["typeof("] = 7 }),
        ["Poly/Interpretation/Analysis/Semantics/ClrTypeResolution.cs"] = ("resolves a CLR type reference; removing row: I8b (#972)", new(StringComparer.Ordinal) { ["GetRuntimeType"] = 1 }),
        ["Poly/Interpretation/Analysis/Semantics/ConstantOperandRule.cs"] = ("CLR type facts; removing row: I8b (#972)", new(StringComparer.Ordinal) { ["typeof("] = 4 }),
        ["Poly/Interpretation/Analysis/Semantics/IteratorLegalityAnalyzer.cs"] = ("CLR type facts; removing row: I8b (#972)", new(StringComparer.Ordinal) { ["GetRuntimeType"] = 1, ["typeof("] = 2 }),
        ["Poly/Interpretation/Analysis/Semantics/LambdaReturnTypeAnalyzer.cs"] = ("CLR type facts; removing row: I8b (#972)", new(StringComparer.Ordinal) { ["GetRuntimeType"] = 1, ["typeof("] = 1 }),
        ["Poly/Interpretation/Analysis/Semantics/SyntaxTypeCompatibilityAnalyzer.cs"] = ("the CLR category table and its type references (T-28); removing row: I8b (#972)", new(StringComparer.Ordinal) { ["CLR type predicate"] = 5, ["GetRuntimeType"] = 14, ["Nullable.GetUnderlyingType"] = 8, ["RuntimeType"] = 3, ["typeof("] = 55 }),
        ["Poly/Interpretation/Analysis/Semantics/TypeAndMemberResolutionPass.cs"] = ("CLR type statements the pass still makes; removing row: I8b (#972)", new(StringComparer.Ordinal) { ["CLR type predicate"] = 1, ["GetRuntimeType"] = 17, ["Nullable.GetUnderlyingType"] = 5, ["RuntimeType"] = 1, ["typeof("] = 31 }),
        ["Poly/Interpretation/Analysis/Semantics/TypeDefinitionNodeAnalyzer.cs"] = ("CLR type facts; removing row: I8b (#972)", new(StringComparer.Ordinal) { ["CLR type predicate"] = 1, ["GetRuntimeType"] = 1, ["Nullable.GetUnderlyingType"] = 1, ["RuntimeType"] = 5, ["typeof("] = 14 }),
    };

    [Test]
    public async Task MeaningLayers_NameNoExecutionReflectionOrClrTypeFact() {
        var repo = GetRepoRoot();
        var namespaceOffenders = new List<string>();
        var seenAllowed = new HashSet<string>(StringComparer.Ordinal);
        var measured = new SortedDictionary<string, SortedDictionary<string, int>>(StringComparer.Ordinal);

        foreach (var relativeRoot in ScannedRoots) {
            foreach (var file in Directory.EnumerateFiles(Path.Combine(repo, relativeRoot), "*.cs", SearchOption.AllDirectories)) {
                var relative = Path.GetRelativePath(repo, file).Replace('\\', '/');
                if (relative.Contains("/obj/") || relative.EndsWith(".g.cs", StringComparison.Ordinal))
                    continue;
                // The host folder is where the CLR keeps its own facts.
                if (relative.StartsWith("Poly/Introspection/CommonLanguageRuntime/", StringComparison.Ordinal))
                    continue;

                var text = await File.ReadAllTextAsync(file);
                if (ForbiddenNamespaces.Any(text.Contains)) {
                    if (AllowedNamespaces.ContainsKey(relative))
                        seenAllowed.Add(relative);
                    else
                        namespaceOffenders.Add(relative);
                }

                var code = StripCommentsAndStrings(text);
                foreach (var (name, pattern) in TypeFacts) {
                    var count = pattern.Matches(code).Count;
                    if (count == 0)
                        continue;
                    if (!measured.TryGetValue(relative, out var perFile))
                        measured[relative] = perFile = new SortedDictionary<string, int>(StringComparer.Ordinal);
                    perFile[name] = count;
                }
            }
        }

        await Assert.That(string.Join(", ", namespaceOffenders)).IsEmpty()
            .Because($"allowed exceptions, each with its removing row: {string.Join("; ", AllowedNamespaces.Select(entry => $"{entry.Key}: {entry.Value}"))}");
        await Assert.That(string.Join("", AllowedNamespaces.Keys.Except(seenAllowed))).IsEmpty()
            .Because("an allowlist entry that no longer offends must go with its row");

        var drift = new List<string>();
        foreach (var (file, perFile) in measured) {
            foreach (var (pattern, count) in perFile) {
                if (!Budget.TryGetValue(file, out var entry) || !entry.Max.TryGetValue(pattern, out var budgeted)) {
                    drift.Add($"{file} [{pattern}]: {count}, not in the budget");
                    continue;
                }
                if (count < budgeted)
                    drift.Add($"{file} [{pattern}]: {count} is below its budget {budgeted}; lower it");
                else if (count > budgeted)
                    drift.Add($"{file} [{pattern}]: {count} is over its budget {budgeted} ({entry.RemovingRow})");
            }
        }
        foreach (var (file, entry) in Budget) {
            foreach (var (pattern, budgeted) in entry.Max) {
                if (!measured.TryGetValue(file, out var actual) || !actual.ContainsKey(pattern))
                    drift.Add($"{file} [{pattern}]: budget {budgeted} but none measured; remove the entry");
            }
        }

        await Assert.That(string.Join("; ", drift)).IsEmpty()
            .Because("the budget is exact: a count may only go down by editing this file in the same change");
    }

    /// <summary>Removes <c>//</c>, <c>/* */</c>, string and char literals, so documentation and messages do not count.</summary>
    private static string StripCommentsAndStrings(string text) {
        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++) {
            var current = text[i];
            if (current == '/' && i + 1 < text.Length && text[i + 1] == '/') {
                while (i < text.Length && text[i] != '\n') i++;
                builder.Append('\n');
                continue;
            }
            if (current == '/' && i + 1 < text.Length && text[i + 1] == '*') {
                i += 2;
                while (i + 1 < text.Length && !(text[i] == '*' && text[i + 1] == '/')) i++;
                i++;
                builder.Append(' ');
                continue;
            }
            if (current == '"') {
                var verbatim = i > 0 && text[i - 1] == '@';
                i++;
                while (i < text.Length) {
                    if (!verbatim && text[i] == '\\') { i += 2; continue; }
                    if (text[i] == '"') {
                        if (verbatim && i + 1 < text.Length && text[i + 1] == '"') { i += 2; continue; }
                        break;
                    }
                    i++;
                }
                builder.Append("\"\"");
                continue;
            }
            if (current == '\'') {
                i++;
                while (i < text.Length && text[i] != '\'') {
                    if (text[i] == '\\') i++;
                    i++;
                }
                builder.Append("''");
                continue;
            }
            builder.Append(current);
        }
        return builder.ToString();
    }

    private static string GetRepoRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
}