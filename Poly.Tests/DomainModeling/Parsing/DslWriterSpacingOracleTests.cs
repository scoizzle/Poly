using Poly.DomainModeling.Language;
using Poly.Tests.Docs;

namespace Poly.Tests.DomainModeling.Parsing;

/// <summary>
/// The spacing oracle (G-27 P4): every line the hand printer prints whose tokens are names, keywords,
/// <c>:</c> <c>,</c> <c>(</c> <c>)</c> <c>-&gt;</c> <c>{</c> <c>}</c> reproduces byte for byte when its
/// tokens are written through <see cref="DslTokenWriter"/>. Names are written as raw text (the default
/// kind), as a table row writes a fill; keywords and punctuation are written by kind. The input is the
/// frozen print baseline (<c>PrintBaseline.txt</c>, G-26a), which is the hand printer's exact output for
/// every shipped document, the guide and the code-built cases (including <c>key(A, B)</c> and
/// <c>initial(S)</c>). The <c>built/entry/</c> keys are printer-internal fragment keys, not DSL, and are skipped.
///
/// <para>An expression is a fill when a row prints it (a Slot, or a named hole), so the oracle writes the
/// text inside an inline <c>{ … }</c> and inside <c>if ( … )</c> as one raw piece: the writer spaces
/// around the expression, never inside it (inside is the expression printer's, G-34).</para>
/// </summary>
public sealed class DslWriterSpacingOracleTests {
    private const string BaselinePath = "Poly.Tests/DomainModeling/Parsing/PrintBaseline.txt";

    private static bool IsKeyword(DslTokenKind kind) =>
        kind is not (DslTokenKind.EndOfFile or DslTokenKind.Identifier or DslTokenKind.Number or DslTokenKind.StringLiteral)
        && char.IsLetter(DslGrammar.CanonicalText(kind)[0]);

    private static bool IsCompared(DslTokenKind kind) =>
        kind is DslTokenKind.Identifier or DslTokenKind.Colon or DslTokenKind.Comma
            or DslTokenKind.LParen or DslTokenKind.RParen or DslTokenKind.Arrow
            or DslTokenKind.LBrace or DslTokenKind.RBrace
        || IsKeyword(kind);

    /// <summary>The distinct printed lines (indent trimmed) whose every token is a compared kind.</summary>
    internal static IReadOnlyList<string> ComparedLines() {
        var lines = new SortedSet<string>(StringComparer.Ordinal);
        var skipSection = false;
        foreach (var raw in File.ReadAllLines(Path.Combine(GuideBlocks.RepoRoot(), BaselinePath))) {
            if (raw.StartsWith("##### ", StringComparison.Ordinal))
                skipSection = raw.StartsWith("##### built/entry/", StringComparison.Ordinal);
            if (raw.StartsWith('#') || skipSection)
                continue;
            var line = raw.Trim();
            if (line.Length == 0)
                continue;
            // A line that is not DSL on its own (a fragment of a multi-line string) is not compared.
            List<DslToken> tokens;
            try {
                tokens = Tokens(line);
            }
            catch (Poly.Grammar.GrammarException) {
                continue;
            }
            if (tokens.Count > 0 && tokens.All(t => IsCompared(t.Kind)))
                lines.Add(line);
        }
        return [.. lines];
    }

    private static List<DslToken> Tokens(string line) {
        var reader = new DslTokenReader(line);
        var tokens = new List<DslToken>();
        for (var i = 0; ; i++) {
            var token = reader.Peek(i);
            if (reader.EndOfStream(token.Kind))
                return tokens;
            tokens.Add(token);
        }
    }

    /// <summary>
    /// Writes a line's tokens through the writer: identifiers as raw text, every other kind by kind, and the
    /// non-empty inside of an inline <c>{ … }</c> or of <c>if ( … )</c> as one raw piece (an expression fill).
    /// </summary>
    internal static string Rewrite(string line) {
        var writer = new DslTokenWriter();
        var tokens = Tokens(line);
        for (var i = 0; i < tokens.Count; i++) {
            var token = tokens[i];
            var close = token.Kind switch {
                DslTokenKind.LBrace => DslTokenKind.RBrace,
                DslTokenKind.LParen when i > 0 && tokens[i - 1].Kind == DslTokenKind.If => DslTokenKind.RParen,
                _ => default,
            };
            if (close != default && Matching(tokens, i, close) is { } end && end > i + 1) {
                writer.Write(token.Kind);
                var from = token.Start + token.Length;
                writer.Write(default, line[from..tokens[end].Start].Trim());
                writer.Write(close);
                i = end;
                continue;
            }
            if (token.Kind == DslTokenKind.Identifier)
                writer.Write(default, token.Span.ToString());
            else
                writer.Write(token.Kind);
        }
        return writer.ToText();
    }

    /// <summary>The index of the token that closes the one at <paramref name="open"/> on this line; null when none does.</summary>
    private static int? Matching(List<DslToken> tokens, int open, DslTokenKind close) {
        var depth = 0;
        for (var j = open; j < tokens.Count; j++) {
            if (tokens[j].Kind == tokens[open].Kind)
                depth++;
            else if (tokens[j].Kind == close && --depth == 0)
                return j;
        }
        return null;
    }

    [Test]
    public async Task HandPrintedHeaderLines_ReproduceFromTokens() {
        var mismatches = ComparedLines()
            .Select(line => (Line: line, Written: Rewrite(line)))
            .Where(pair => !string.Equals(pair.Line, pair.Written, StringComparison.Ordinal))
            .Select(pair => $"printed '{pair.Line}' / written '{pair.Written}'")
            .ToList();

        await Assert.That(string.Join("\n", mismatches)).IsEqualTo("");
    }

    [Test]
    public async Task ComparedLines_CoverEveryRuleConsumer() {
        var lines = ComparedLines();

        var missing = new[] { "key(Name, ", "public action ", "initial(", " via ", ": action (", " action ", ") -> ", ", ", ") key(", " {", "{ ", " }", "{}", "if (", "else {" }
            .Where(needle => !lines.Any(line => line.Contains(needle, StringComparison.Ordinal)))
            .ToList();

        await Assert.That(lines.Count).IsGreaterThanOrEqualTo(1000);
        await Assert.That(missing).IsEmpty();
    }
}
