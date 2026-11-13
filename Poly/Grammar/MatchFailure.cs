using System.Text;
using System.Text.RegularExpressions;

namespace Poly.Grammar;

/// <summary>
/// What <see cref="Matcher{TToken,TTokenKind}.Match"/> found: the match, or the failure the table reports. Exactly one is
/// non-null.
/// </summary>
public readonly record struct MatchOutcome<TToken, TTokenKind>(
    MatchResult<TToken, TTokenKind>? Result,
    MatchFailure<TToken, TTokenKind>? Failure)
    where TToken : struct, IToken<TTokenKind>
    where TTokenKind : struct;

/// <summary>
/// One element the failure expected at the failing token: where it is in the table (<see cref="Rule"/>,
/// <see cref="Pattern"/>, the zero-based <see cref="Element"/> index in the row), what to call it
/// (<see cref="Label"/>: the element's <c>Expect</c>, a Word's quoted text or a Run's name; null for an
/// unlabelled element, which the host names by <see cref="Kind"/>), and whether it is a content element
/// (a value, predicate, Run or Any: runtime text) rather than fixed syntax (a kind, a Word, a Balanced delimiter).
/// </summary>
public sealed record Expectation<TTokenKind>(
    string Rule,
    string Pattern,
    int Element,
    string? Label,
    TTokenKind? Kind,
    bool IsContent)
    where TTokenKind : struct;

/// <summary>
/// The failure the table reports for one <see cref="Matcher{TToken,TTokenKind}.Match"/> call: the failing
/// <see cref="Token"/> (the host reads its position), what was expected there in declaration order, and the
/// row's message when it carries one. The engine never words a message: the host supplies the default
/// wording, the join of the expected set and the token text (<see cref="Expand"/> fills a row's own template).
/// <para>
/// A committed failure (a row failed after its <see cref="Cut{TToken,TTokenKind}"/>, or reached its
/// <see cref="Refuse{TToken,TTokenKind}"/>) reports the committed failures only: the furthest, unioned at that
/// offset. Otherwise the report is the furthest plain failure, unioned at that offset.
/// </para>
/// </summary>
public sealed partial class MatchFailure<TToken, TTokenKind>
    where TToken : struct, IToken<TTokenKind>
    where TTokenKind : struct {
    internal MatchFailure(
        TToken token,
        int offset,
        bool committed,
        bool refused,
        string rule,
        string pattern,
        int element,
        IReadOnlyList<Expectation<TTokenKind>> expected,
        string? template,
        IReadOnlyDictionary<string, IReadOnlyList<TToken>> captures) {
        Token = token;
        Offset = offset;
        Committed = committed;
        Refused = refused;
        Rule = rule;
        Pattern = pattern;
        Element = element;
        Expected = expected;
        Template = template;
        Captures = captures;
    }

    /// <summary>The failing token: the first token no expected element matched, or a refusal's last matched token.</summary>
    public TToken Token { get; }

    /// <summary>The failing token's offset from the reader's committed position.</summary>
    public int Offset { get; }

    /// <summary>True when a row failed after its Cut, or reached its Refuse.</summary>
    public bool Committed { get; }

    /// <summary>True when the report is a <see cref="Refuse{TToken,TTokenKind}"/> row's: <see cref="Template"/> is its message.</summary>
    public bool Refused { get; }

    /// <summary>
    /// The row that committed (its Cut passed, or it reached its Refuse); the first in declaration order when siblings
    /// committed at the same offset. Null for a plain failure. A caller that must not throw (an outline) can read
    /// the kind of a half-written head from it.
    /// </summary>
    public string? CommittedPattern { get; internal set; }

    /// <summary>The rule of the reported element (the first in declaration order); empty when nothing was recorded.</summary>
    public string Rule { get; }

    /// <summary>The pattern of the reported element; empty when nothing was recorded.</summary>
    public string Pattern { get; }

    /// <summary>The zero-based index of the reported element in its row; -1 when nothing was recorded.</summary>
    public int Element { get; }

    /// <summary>
    /// The elements expected at <see cref="Token"/>, sorted by (declaration order of the row, element index) and
    /// deduplicated. Empty when what failed names nothing (a negative lookahead, an unlabelled predicate).
    /// </summary>
    public IReadOnlyList<Expectation<TTokenKind>> Expected { get; }

    /// <summary>
    /// The row's own message, unexpanded: the template of the first expected element that has one, or the refusal
    /// message. Null when the host's default applies.
    /// </summary>
    public string? Template { get; }

    /// <summary>The captures of the reported element's row at the failure (what a template's capture placeholders read).</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<TToken>> Captures { get; }

    /// <summary>
    /// Fills <see cref="Template"/>: <c>{expected}</c> with <paramref name="expected"/> (the host's join of
    /// <see cref="Expected"/>), <c>{got}</c> with <paramref name="shown"/> of <see cref="Token"/>, and a capture name
    /// with its tokens shown and joined by a space. Throws when there is no template.
    /// </summary>
    public string Expand(string expected, Func<TToken, string> shown) {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(shown);
        if (Template is null)
            throw new InvalidOperationException("This failure carries no row template; the host words the message.");
        return Placeholder().Replace(Template, m => m.Groups[1].Value switch {
            "expected" => expected,
            "got" => shown(Token),
            var name when Captures.TryGetValue(name, out var tokens) => string.Join(' ', tokens.Select(shown)),
            var name => throw new InvalidOperationException(
                $"Template placeholder '{{{name}}}' of {Rule}/{Pattern} names no capture of the row at this failure."),
        });
    }

    /// <summary>The placeholders a template may hold: <c>{expected}</c>, <c>{got}</c>, or a capture name.</summary>
    internal static IEnumerable<string> PlaceholdersOf(string template) =>
        Placeholder().Matches(template).Select(m => m.Groups[1].Value);

    [GeneratedRegex(@"\{([A-Za-z][A-Za-z0-9-]*)\}")]
    private static partial Regex Placeholder();

    public override string ToString() {
        var sb = new StringBuilder();
        sb.Append(Committed ? "committed " : "").Append("failure at ").Append(Offset);
        if (Rule.Length > 0)
            sb.Append(" (").Append(Rule).Append('/').Append(Pattern).Append('#').Append(Element).Append(')');
        sb.Append(": expected ").Append(string.Join(" | ", Expected.Select(e => e.Label ?? e.Kind?.ToString())));
        return sb.ToString();
    }
}

/// <summary>One reported failure of a <c>Match</c> call, as <see cref="MatchTrace"/> records it (observation only).</summary>
/// <param name="Rule">The rule the call matched.</param>
/// <param name="Failure">The <see cref="MatchFailure{TToken,TTokenKind}"/> the call returned.</param>
internal sealed record MatchObservation(string Rule, object Failure);

/// <summary>
/// An opt-in observer of the failures <see cref="Matcher{TToken,TTokenKind}.Match"/> returns, scoped to the async
/// flow that began it, like <see cref="PrintTrace"/>. Nothing records outside a scope, and a successful match never
/// records. G-26b's diagnostics oracle begins a scope around each resolve so a table-reported error names the row
/// element that produced it (label reach).
/// </summary>
internal static class MatchTrace {
    private static readonly AsyncLocal<Scope?> Active = new();

    internal static Scope Begin() {
        var scope = new Scope(Active.Value);
        Active.Value = scope;
        return scope;
    }

    internal static void Record(MatchObservation observation) => Active.Value?.Add(observation);

    internal sealed class Scope : IDisposable {
        private readonly Scope? _previous;
        private readonly Lock _gate = new();
        private readonly List<MatchObservation> _observations = [];
        private bool _disposed;

        internal Scope(Scope? previous) => _previous = previous;

        /// <summary>A snapshot of the observations recorded so far, in record order.</summary>
        public IReadOnlyList<MatchObservation> Observations {
            get {
                lock (_gate)
                    return [.. _observations];
            }
        }

        internal void Add(MatchObservation observation) {
            lock (_gate) {
                if (!_disposed)
                    _observations.Add(observation);
            }
        }

        public void Dispose() {
            lock (_gate) {
                if (_disposed)
                    return;
                _disposed = true;
            }
            if (ReferenceEquals(Active.Value, this))
                Active.Value = _previous;
        }
    }
}