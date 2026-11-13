using System.Text.RegularExpressions;

using Poly.DomainModeling.Language;
using Poly.Grammar;
using Poly.Tests.Docs;

namespace Poly.Tests.DomainModeling.Parsing;

/// <summary>
/// Some consumers read a matched token by the row's shape, not by a kind test of their own: the effect
/// handlers read <c>match.Tokens[n].Name()</c>, the expression parser and the temporal fold read named
/// captures (<c>Name()</c> for words, the span for <c>amount</c>), and the fold table reads the first token
/// of its <c>string</c>/<c>ident</c>/<c>number</c> patterns. The row is the kind proof, so a row edit that moves
/// an index or retypes a capture must fail here, loudly, and not as an unpositioned
/// <see cref="InvalidOperationException"/> from <c>Name()</c> at parse time. Each table is checked against a
/// source scan of its reader, so a new read site that is not in the table fails too.
/// </summary>
public sealed class ReaderRowShapeTests {
    /// <summary>(rule, pattern, token index) the effect handlers in <c>DomainResolver.Effects.cs</c> read with <c>Name()</c>.</summary>
    private static readonly (string Rule, string Pattern, int Index)[] EffectIndexReads = [
        ("effect", "transition", 2),
        ("effect", "assign", 1),
        ("effect", "link", 1),
        ("effect", "unlink", 1),
        ("effect", "create-in", 2),
        ("effect", "create", 1),
    ];

    /// <summary>
    /// (rule, pattern, token index) the entity body reads with <c>Name()</c> as <c>Current</c> after consuming the
    /// tokens before it: the typed-line type word.
    /// </summary>
    private static readonly (string Rule, string Pattern, int Index)[] EntityIndexReads = [
        ("entity-body", "typed-line", 2),
    ];

    /// <summary>Capture names read as text, and the one kind every element with that name may match.</summary>
    private static readonly Dictionary<string, DslTokenKind> CaptureReads = new(StringComparer.Ordinal) {
        ["rel"] = DslTokenKind.Identifier,
        ["prop"] = DslTokenKind.Identifier,
        ["hop"] = DslTokenKind.Identifier,
        ["quant"] = DslTokenKind.Identifier,
        ["unit"] = DslTokenKind.Identifier,
        ["amount"] = DslTokenKind.Number,
    };

    /// <summary>The fold table's patterns that read their first token's text, and its kind.</summary>
    private static readonly Dictionary<string, DslTokenKind> FoldFirstTokenReads = new(StringComparer.Ordinal) {
        ["string"] = DslTokenKind.StringLiteral,
        ["ident"] = DslTokenKind.Identifier,
        ["number"] = DslTokenKind.Number,
    };

    private const string ProbeText = "zz 7 \"Days\" \"any\" \"sum\" \"count\" \"where\" \"x\" entity -> Text Number in where Days";

    private static List<DslToken> Probes() {
        var reader = new DslTokenReader(ProbeText);
        var tokens = new List<DslToken>();
        for (var t = reader.Peek(); t.Kind != DslTokenKind.EndOfFile; t = reader.Peek()) {
            tokens.Add(t);
            reader.Consume(1);
        }

        return tokens;
    }

    /// <summary>Null when the element can match only a token of <paramref name="kind"/>; otherwise why not.</summary>
    private static string? AdmitsOnly(IPatternElement<DslToken, DslTokenKind> element, DslTokenKind kind) {
        switch (element) {
            case Value<DslToken, DslTokenKind> value:
                return value.Kind == kind ? null : $"Value({value.Kind})";
            case MatchKind<DslToken, DslTokenKind> match:
                return match.Kind == kind ? null : $"Kind({match.Kind})";
            case MatchPredicate<DslToken, DslTokenKind> predicate: {
                    var admitted = Probes().Where(t => t.Kind != kind && predicate.Predicate(t)).Select(t => $"{t.Kind}:{t.Span}").ToList();
                    return admitted.Count == 0 ? null : $"Predicate({predicate.Label}) admits {string.Join(", ", admitted)}";
                }
            default:
                return element.GetType().Name;
        }
    }

    private static string? CaptureName(IPatternElement<DslToken, DslTokenKind> element) => element switch {
        Value<DslToken, DslTokenKind> value => value.Name,
        MatchPredicate<DslToken, DslTokenKind> predicate => predicate.Label,
        _ => null,
    };

    private static bool IsSingleToken(IPatternElement<DslToken, DslTokenKind> element) =>
        element is Value<DslToken, DslTokenKind> or MatchKind<DslToken, DslTokenKind> or MatchPredicate<DslToken, DslTokenKind>;

    private static string Source(string relative) => File.ReadAllText(Path.Combine(GuideBlocks.RepoRoot(), relative));

    [Test]
    public async Task IndexReads_AreIdentifierValues_AtTheirTokenIndex() {
        var failures = new List<string>();
        foreach (var (rule, pattern, index) in EffectIndexReads.Concat(EntityIndexReads)) {
            if (!DslGrammar.Core.TryGetPattern(rule, pattern, out var row)) {
                failures.Add($"{rule}/{pattern}: no such row");
                continue;
            }
            // The token index equals the element index only while every earlier element is exactly one token.
            if (row.Elements.Count <= index || row.Elements.Take(index + 1).Any(e => !IsSingleToken(e))) {
                failures.Add($"{rule}/{pattern}: elements before [{index}] are not one token each");
                continue;
            }
            if (row.Elements[index] is not Value<DslToken, DslTokenKind> { Kind: DslTokenKind.Identifier })
                failures.Add($"{rule}/{pattern}[{index}]: {row.Elements[index]}");
        }

        await Assert.That(failures).IsEmpty();
    }

    [Test]
    public async Task EffectIndexReads_TableCoversEverySourceRead() {
        var reads = Regex.Matches(Source("Poly/DomainModeling/Language/DomainResolver.Effects.cs"), @"match\.Tokens\[(\d+)\]")
            .Select(m => int.Parse(m.Groups[1].Value)).ToList();

        await Assert.That(reads.Count).IsEqualTo(EffectIndexReads.Length);
        await Assert.That(reads.Order()).IsEquivalentTo(EffectIndexReads.Select(r => r.Index).Order());
    }

    [Test]
    public async Task CaptureReads_EveryElementWithTheNameAdmitsOnlyItsKind() {
        var failures = new List<string>();
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var rule in DslGrammar.Core.KnownRules) {
            foreach (var row in DslGrammar.Core.GetPatterns(rule)) {
                foreach (var element in row.Elements) {
                    if (CaptureName(element) is not { } name || !CaptureReads.TryGetValue(name, out var kind))
                        continue;
                    seen[name] = seen.GetValueOrDefault(name) + 1;
                    if (AdmitsOnly(element, kind) is { } why)
                        failures.Add($"{rule}/{row.Name} capture '{name}': {why}");
                }
            }
        }

        await Assert.That(failures).IsEmpty();
        // Not vacuous: every capture read by name occurs in the table.
        await Assert.That(seen.Keys.Order(StringComparer.Ordinal)).IsEquivalentTo(CaptureReads.Keys.Order(StringComparer.Ordinal));
    }

    [Test]
    public async Task CaptureReads_TableCoversEverySourceRead() {
        var parser = Regex.Matches(Source("Poly/DomainModeling/Language/DslExpressionParser.cs"), @"Capture(?:Text|Token)\(\w+, ""(\w+)""\)")
            .Select(m => m.Groups[1].Value);
        var temporal = Regex.Matches(Source("Poly/DomainModeling/Libraries/Temporal/TemporalExpressionPrintBinders.cs"), @"Captures\.TryGetValue\(""(\w+)""")
            .Select(m => m.Groups[1].Value);
        var read = parser.Concat(temporal).ToHashSet(StringComparer.Ordinal);

        await Assert.That(read.Order(StringComparer.Ordinal)).IsEquivalentTo(CaptureReads.Keys.Order(StringComparer.Ordinal));
    }

    [Test]
    public async Task CallName_IsTheFirstTokenAndAnIdentifier() {
        var failures = new List<string>();
        foreach (var rule in new[] { "expr-primary", "expr-primary-no-not" }) {
            if (!DslGrammar.Core.TryGetPattern(rule, "call", out var row))
                failures.Add($"{rule}/call: no such row");
            else if (AdmitsOnly(row.Elements[0], DslTokenKind.Identifier) is { } why)
                failures.Add($"{rule}/call[0]: {why}");
        }

        await Assert.That(failures).IsEmpty();
    }

    [Test]
    public async Task FoldFirstTokenReads_EveryRowWithThePatternNameStartsWithItsKind() {
        var failures = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in DslGrammar.Core.KnownRules) {
            foreach (var row in DslGrammar.Core.GetPatterns(rule)) {
                if (!FoldFirstTokenReads.TryGetValue(row.Name, out var kind))
                    continue;
                seen.Add(row.Name);
                if (row.Elements.Count == 0 || AdmitsOnly(row.Elements[0], kind) is { } why)
                    failures.Add($"{rule}/{row.Name}[0]: {(row.Elements.Count == 0 ? "empty" : AdmitsOnly(row.Elements[0], kind))}");
            }
        }

        var registered = Regex.Matches(Source("Poly/DomainModeling/Meaning/ExpressionFoldTable.cs"), @"table\.Register\(""(\w+)""")
            .Select(m => m.Groups[1].Value).ToList();

        await Assert.That(failures).IsEmpty();
        await Assert.That(seen.Order(StringComparer.Ordinal)).IsEquivalentTo(FoldFirstTokenReads.Keys.Order(StringComparer.Ordinal));
        // The folds that read text are the registered ones minus the constant folds (true, false, null).
        await Assert.That(registered.Except(["true", "false", "null"]).Order(StringComparer.Ordinal))
            .IsEquivalentTo(FoldFirstTokenReads.Keys.Order(StringComparer.Ordinal));
    }

    [Test]
    public async Task Probes_CoverEveryKindAPredicateCouldWronglyAdmit() {
        var kinds = Probes().Select(t => t.Kind).Distinct().ToList();

        await Assert.That(kinds).Contains(DslTokenKind.Identifier);
        await Assert.That(kinds).Contains(DslTokenKind.Number);
        await Assert.That(kinds).Contains(DslTokenKind.StringLiteral);
        await Assert.That(kinds).Contains(DslTokenKind.Entity);
        await Assert.That(kinds).Contains(DslTokenKind.Arrow);
        await Assert.That(kinds).Contains(DslTokenKind.Text);
        await Assert.That(kinds).Contains(DslTokenKind.In);
    }
}