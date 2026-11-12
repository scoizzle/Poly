using Poly.DomainModeling.Language;

namespace Poly.Tests.Grammar;

/// <summary>
/// The grammar change protocol (theme #678 policy g.2), as far as a test can enforce it.
///
/// <para>The checklist a new-syntax slice satisfies: token kind + <c>CanonicalText</c>
/// row + grammar pattern row + DomainResolver/DslExpressionParser fold + printer fill
/// + round-trip case + fail-closed case + guide section.</para>
///
/// <para>This class enforces the two rungs a machine can read from the type system:
/// the token kind, and the canonical text that is the tokenizer's inverse. The pattern
/// row, the fold, the printer fill, the fail-closed case and the guide line stay review
/// rungs — they have no reflection surface, so a reviewer names them in the PR.</para>
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
}