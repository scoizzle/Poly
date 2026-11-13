using Poly.DomainModeling.Language;
using Poly.Grammar;
using Poly.Interpretation.CSharp;

namespace Poly.Tests.Grammar;

/// <summary>
/// The grammar change protocol (theme #678 policy g.2), as far as a test can enforce it.
///
/// <para>The checklist a new-syntax slice satisfies: token kind + <c>CanonicalText</c>
/// row + grammar pattern row + DomainResolver/DslExpressionParser fold + printer fill
/// + round-trip case + fail-closed case + guide section.</para>
///
/// <para>This class enforces four rungs. Two are read from the type system: the token kind,
/// and the canonical text that is the tokenizer's inverse. Two are read from the printers
/// (G-23): every pattern row against its printer fill, on both table surfaces (the DSL and
/// C#). The fold, the fail-closed case and the guide line stay review rungs — they have no
/// reflection surface, so a reviewer names them in the PR.</para>
///
/// <para><b>Pattern rows and printer fills.</b> The drive (<see cref="PatternRowCorpus"/>)
/// prints both corpora under a <see cref="Poly.Grammar.PrintTrace"/> scope; the pure
/// <see cref="PatternRowChecker"/> checks every observed print: its holes and slot count
/// (A1), its read-back through the full grammar and the reader's own tokenizer (A2), and
/// coverage of rows, optional slots and reader-head examples (A3). A missing
/// labelled-predicate fill is caught by A2 only: the hole check is circular for predicate
/// labels, because an unfilled label is a positional slot. Exemptions are the keyed
/// <see cref="PatternRowAllowList"/>, checked both ways.</para>
///
/// <para>A slice that adds or changes a pattern row adds an observation for it (a corpus
/// line, or an example for a reader head) and deletes its stale allow-list entry in the
/// same change.</para>
///
/// <para>Value-bearing kinds (content positions whose text a handler supplies) have no
/// canonical text, which is what the exemption list below says. The list is local to
/// this test on purpose: no G row consumes a production marker, and <c>DslGrammar.cs</c>
/// carries a long writer queue that this slice is not in.</para>
/// </summary>
public sealed class GrammarChangeProtocolTests {
    /// <summary>
    /// Kinds whose token text is content, not spelling: the printer fills them from the
    /// domain, so they cannot have a canonical text that is a fixed point.
    /// </summary>
    private static readonly DslTokenKind[] ValueBearing = [
        DslTokenKind.EndOfFile,
        DslTokenKind.Identifier,
        DslTokenKind.Number,
        DslTokenKind.StringLiteral,
    ];

    private static IEnumerable<DslTokenKind> FixedKinds() =>
        Enum.GetValues<DslTokenKind>().Where(kind => !ValueBearing.Contains(kind));

    /// <summary>
    /// (a) Every kind is either value-bearing or has a <c>CanonicalText</c> row, so a
    /// new token kind cannot land without its spelling. The switch throws
    /// <see cref="ArgumentOutOfRangeException"/> for a missing row, so the miss is
    /// reported rather than ignored.
    /// </summary>
    [Test]
    public async Task EveryDslTokenKind_HasCanonicalText_OrIsValueBearing() {
        var violations = new List<string>();

        foreach (var kind in Enum.GetValues<DslTokenKind>()) {
            if (ValueBearing.Contains(kind)) continue;
            try {
                DslGrammar.CanonicalText(kind);
            }
            catch (ArgumentOutOfRangeException) {
                violations.Add($"{kind}: no CanonicalText row and not value-bearing");
            }
        }

        await Assert.That(violations).IsEmpty();
    }

    /// <summary>
    /// (b) Every row's text is the tokenizer's inverse: it reads back as exactly that
    /// kind, consumes the whole row, and leaves <c>EndOfFile</c>. The length check
    /// catches a row carrying stray whitespace (<c>"to "</c>).
    /// </summary>
    [Test]
    public async Task EveryCanonicalText_IsTheTokenizerInverse() {
        var violations = new List<string>();

        foreach (var kind in FixedKinds()) {
            string text;
            try {
                text = DslGrammar.CanonicalText(kind);
            }
            catch (ArgumentOutOfRangeException) {
                violations.Add($"{kind}: no canonical text to read back");
                continue;
            }

            try {
                var reader = new DslTokenReader(text);
                var token = reader.Peek();
                if (token.Kind != kind)
                    violations.Add($"{kind}: '{text}' tokenizes to {token.Kind}");
                if (token.Length != text.Length)
                    violations.Add($"{kind}: '{text}' yields a {token.Length}-char token");

                reader.Consume(1);
                var next = reader.Peek();
                if (next.Kind != DslTokenKind.EndOfFile)
                    violations.Add($"{kind}: '{text}' leaves {next.Kind} unread");
            }
            catch (FormatException ex) {
                violations.Add($"{kind}: '{text}' throws {ex.Message}");
            }
        }

        await Assert.That(violations).IsEmpty();
    }

    /// <summary>
    /// (c) No two rows share a text, so a copy-pasted row cannot pass the coverage check
    /// while binding the wrong kind.
    /// </summary>
    [Test]
    public async Task EveryCanonicalText_IsUnique() {
        var violations = new List<string>();
        var rows = new List<(DslTokenKind Kind, string Text)>();

        foreach (var kind in FixedKinds()) {
            try {
                rows.Add((kind, DslGrammar.CanonicalText(kind)));
            }
            catch (ArgumentOutOfRangeException) {
                // The coverage test names the missing row.
            }
        }

        foreach (var group in rows.GroupBy(row => row.Text, StringComparer.Ordinal).Where(g => g.Count() > 1))
            violations.Add($"'{group.Key}' is the canonical text of {string.Join(", ", group.Select(row => row.Kind))}");

        await Assert.That(violations).IsEmpty();
    }

    /// <summary>
    /// (d) A value-bearing kind has no row. A row for one of them means either the kind
    /// is not value-bearing (drop the exemption) or the row is wrong (drop the row), so
    /// the exemption list cannot drift away from the switch.
    /// </summary>
    [Test]
    public async Task EveryValueBearingKind_HasNoCanonicalText() {
        var violations = new List<string>();

        foreach (var kind in ValueBearing) {
            string? text = null;
            try {
                text = DslGrammar.CanonicalText(kind);
            }
            catch (ArgumentOutOfRangeException) {
                // Expected: the kind is a content position.
            }

            if (text is not null)
                violations.Add($"{kind} is value-bearing but returns canonical text '{text}'");
        }

        await Assert.That(violations).IsEmpty();
    }

    private static IReadOnlyList<PrintObservation> ObservationsOf(object grammar) =>
        [.. PatternRowCorpus.Observations.Where(o => ReferenceEquals(o.Grammar, grammar))];

    private static IReadOnlyList<PatternRowViolation> CheckDsl(Grammar<DslToken, DslTokenKind> grammar) =>
        PatternRowChecker.Check(
            PatternRowCorpus.DslSurface(grammar),
            ObservationsOf(DslGrammar.Core),
            PatternRowAllowList.For(PatternRowAllowList.Dsl),
            PatternRowExamples.For(PatternRowAllowList.Dsl));

    private static IReadOnlyList<PatternRowViolation> CheckCSharp() =>
        PatternRowChecker.Check(
            PatternRowCorpus.CSharpSurface(CSharpGrammar.Table),
            ObservationsOf(CSharpGrammar.Table),
            PatternRowAllowList.For(PatternRowAllowList.CSharp),
            PatternRowExamples.For(PatternRowAllowList.CSharp));

    /// <summary>
    /// (e) Every pattern row of both tables against its printer fill: no violation on the real
    /// tables, with the allow-list as it stands.
    /// </summary>
    [Test]
    public async Task Item18_GrammarChangeProtocol_ChecksPatternRowsAgainstPrinterFills() {
        var violations = new List<string>();
        violations.AddRange(CheckDsl(DslGrammar.Core).Select(v => $"dsl {v}"));
        violations.AddRange(CheckCSharp().Select(v => $"csharp {v}"));

        await Assert.That(violations).IsEmpty();
    }

    /// <summary>
    /// (f) A synthetic pattern row with no printer fill fails the check: it is the only violation
    /// the real drive reports against a table that adds it.
    /// </summary>
    [Test]
    public async Task Item18_SyntheticRowWithNoPrinterFill_FailsTheProtocolCheck() {
        var widened = DslGrammar.Core.ToBuilder()
            .Define("synthetic-row").Pattern("no-fill").Kind(DslTokenKind.Domain).Commit()
            .Build();

        var violations = CheckDsl(widened);

        await Assert.That(violations.Select(v => $"{v.Check} {v.Rule}/{v.Pattern}"))
            .IsEquivalentTo(new[] { "Unobserved synthetic-row/no-fill" });
    }

    /// <summary>
    /// (g) The floors: the drive observes the named rows on each surface. A floor row's name is
    /// first looked up in the table, so renaming one fails here instead of shrinking the floor.
    /// </summary>
    [Test]
    public async Task Item18_DriveObservesTheFloorRows() {
        var violations = new List<string>();
        violations.AddRange(PatternRowChecker.CheckFloor(
            PatternRowCorpus.DslSurface(DslGrammar.Core), ObservationsOf(DslGrammar.Core), PatternRowFloors.Dsl)
            .Select(v => $"dsl {v}"));
        violations.AddRange(PatternRowChecker.CheckFloor(
            PatternRowCorpus.CSharpSurface(CSharpGrammar.Table), ObservationsOf(CSharpGrammar.Table), PatternRowFloors.CSharp)
            .Select(v => $"csharp {v}"));

        await Assert.That(violations).IsEmpty();
    }

    /// <summary>
    /// (h) Every observation belongs to a checked table, so a print from another grammar cannot
    /// pass by being filtered out.
    /// </summary>
    [Test]
    public async Task Item18_EveryObservation_IsOfACheckedTable() {
        var observations = PatternRowCorpus.Observations;
        var fromOtherTables = observations
            .Where(o => !ReferenceEquals(o.Grammar, DslGrammar.Core) && !ReferenceEquals(o.Grammar, CSharpGrammar.Table))
            .Select(o => $"{o.Rule}/{o.Pattern}: {o.Text}")
            .ToList();

        await Assert.That(observations).IsNotEmpty();
        await Assert.That(fromOtherTables).IsEmpty();
    }
}