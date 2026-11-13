using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using Poly.DomainModeling.Language;
using Poly.Grammar;
using Poly.Tests.Docs;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Parsing;

/// <summary>
/// G-12's preservation oracle for the DSL tokenizer: a frozen dump of what <see cref="DslTokenReader"/>,
/// <see cref="DslCursor"/> and the <see cref="DslSyntaxTree"/> header produced before the token moved onto
/// spans. The file (<c>DslTokenStreamBaseline.txt</c>) is DATA written by the pre-change tokenizer; every
/// record is re-derived from its own key and must match byte for byte.
///
/// <para>Records: <c>H</c> a hostile input with one line per token (kind, start, length, line, column,
/// text) or the scan error; <c>U</c> a <c>uses</c> header (Name, Uses, UsesVersions, HeaderEnd);
/// <c>C</c> a cursor <c>Expect</c>/<c>ExpectIdentifier</c> result or message; <c>D</c> a compilable golden
/// domain and <c>E</c>/<c>X</c> an expression of <c>ExprDifferentialBaseline.txt</c> (text or generator
/// spec), each as the line count and SHA of the same per-token dump.</para>
///
/// <para>Regeneration (<c>POLY_REGEN_TOKEN_BASELINE=1</c>) rewrites the file from the CURRENT tokenizer.
/// Never regenerate on a changed tokenizer: that would move the oracle with the code.</para>
/// </summary>
public sealed class DslTokenStreamBaselineTests {
    private const string BaselinePath = "Poly.Tests/DomainModeling/Parsing/DslTokenStreamBaseline.txt";
    private const string ExprBaselinePath = "Poly.Tests/DomainModeling/Parsing/ExprDifferentialBaseline.txt";

    private static readonly string[] KeywordSpellings = [
        "domain", "uses", "entity", "stage", "action", "policy", "when", "require", "transition", "to",
        "assign", "and", "or", "not", "is", "true", "false", "null", "Text", "Number", "Boolean", "optional",
        "unique", "range", "length", "pattern", "enum", "default", "equals", "relationship", "from", "link",
        "unlink", "one", "many", "owned", "create", "in", "invoke", "for", "if", "else", "entry", "exit", "as",
        "public",
    ];

    private static IEnumerable<string> HostileInputs() {
        string[] fixedInputs = [
            "", "\uFEFF", "\uFEFFdomain A", "\uFEFF\nx", "domain A\r\nX: entity {\r\n  N: Text\r\n}\r\n",
            "a\rb", "a\r\rb\n", "\ta\t\tb", "x // comment at EOF", "// only a comment", "a // c\r\nb", "a//b\n//\nc",
            "\"\"", "\"", "\"abc", "\"abc\\\"", "\"abc\\", "\"a\\\"b\"", "\"a\\\\\"", "\"a\\n\"", "\"a\nb\" c",
            "\"a\r\nb\" c", "\"\\\"\"", "\"\\\\\\\"\" x", "\"\\x\" y", "\"\u200B\" z", "\"caf\u00E9\"", "x\"y\"z",
            "1.", "1.5", "1.5.3", "007", "1..2", ".5", "1.x", "\u0661\u0662\u0663", "\u0661.\u0662", "3Days",
            "caf\u00E9 \u03A9mega \u540D\u524D", "_x", "x1", "_", "__a_1", "x\u0661", "a_b.c_d",
            "->", ">=", "<=", "==", "!=", "=", ">", "<", "+", "-", "*", "/", "- >", "-->", ">==", "=>", "<-",
            ": , . ( ) { } [ ]", "a->b", "a>=-1", "x.y.z", "{}", "[x]",
            "!", "! =", "@", "#", "$", "\u200B", "\u00A0", "a @ b", "a\uFEFFb", "x\n  ;", "\u0001", "\uD83D\uDE00",
            "domain A\n\nuses http \"v1\"\n\nX: entity {\n  N: Number range(0.01, 1.0)\n  T: Text optional\n}\n",
        ];
        foreach (var input in fixedInputs)
            yield return input;

        yield return string.Join(' ', KeywordSpellings);
        yield return string.Join(' ', KeywordSpellings.Select(Capitalised));
        yield return string.Join(' ', KeywordSpellings.Select(w => w.ToUpperInvariant()));
        yield return "Domain TEXT Number number text boolean BOOLEAN Default EQUALS";
    }

    private static string Capitalised(string word) =>
        char.IsUpper(word[0]) ? char.ToLowerInvariant(word[0]) + word[1..] : char.ToUpperInvariant(word[0]) + word[1..];

    private static readonly string[] UsesHeaders = [
        "domain A\nuses http \"v1\"\n",
        "domain A\nuses http \"v\\\"1\"\n",
        "domain A\nuses temporal 2024-11-20\n",
        "domain A\nuses http 1.2.3\n",
        "domain A\nuses http 1.2 .3\n",
        "domain A\nuses http\nuses http\n",
        "domain A\nuses http v1\nuses temporal\n",
        "domain A\nuses http\n1.2\n",
        "domain A\n",
        "entity A\n",
    ];

    private static readonly (string Op, string Input)[] CursorCases = [
        ("Expect:Colon", ":"), ("Expect:Colon", "x"), ("Expect:Colon", "\"a\\\"b\""), ("Expect:Colon", ""),
        ("Expect:Colon", "   \n  entity"), ("Expect:Colon", "42"), ("Expect:Colon", "->"),
        ("ExpectIdentifier:Identifier:an entity name", "Task"), ("ExpectIdentifier:Identifier:an entity name", ":"),
        ("ExpectIdentifier:Identifier:an entity name", "\"a\\\"b\""), ("ExpectIdentifier:Identifier:an entity name", ""),
        ("ExpectIdentifier:Identifier:an entity name", "entity"), ("ExpectIdentifier:Identifier:an entity name", "1.5"),
    ];

    // ── dumps ─────────────────────────────────────────────────────────────────────────

    /// <summary>One line per token of <paramref name="input"/>, ending at EOF or at the scan error.</summary>
    private static List<string> TokenDump(string input) {
        var lines = new List<string>();
        var reader = new DslTokenReader(input);
        while (true) {
            DslToken t;
            try {
                t = reader.Peek();
            }
            catch (GrammarException ex) {
                lines.Add($"ERR\t{Escape(ex.Detail)}\t{ex.Line}\t{ex.Column}\t{ex.Offset}\t{ex.Length}");
                return lines;
            }

            lines.Add($"{t.Kind}\t{t.Offset}\t{t.Length}\t{t.Line}\t{t.Col}\t{Escape(t.Text)}");
            if (t.Kind == DslTokenKind.EndOfFile)
                return lines;
            reader.Consume(1);
        }
    }

    private static string HeaderResult(string input) {
        try {
            var tree = DslSyntaxTree.Parse(input);
            var versions = string.Join("|", tree.UsesVersions.Select(v => v is null ? "<none>" : $"[{Escape(v)}]"));
            return $"name=[{Escape(tree.Name)}]\tuses=[{Escape(string.Join("|", tree.Uses))}]\tversions={versions}\theaderEnd={tree.HeaderEnd}";
        }
        catch (Exception ex) {
            return $"ERR {ex.GetType().Name}: {Escape(ex.Message)}";
        }
    }

    private static string CursorResult(string op, string input) {
        var reader = new DslTokenReader(input);
        var cursor = new DslCursor(reader, new Matcher<DslToken, DslTokenKind>(DslGrammar.Core, reader));
        var parts = op.Split(':');
        try {
            return parts[0] switch {
                "Expect" => Ok(cursor.Expect(Enum.Parse<DslTokenKind>(parts[1]))),
                "ExpectIdentifier" => $"OK [{Escape(cursor.ExpectIdentifier(Enum.Parse<DslTokenKind>(parts[1]), parts[2]))}] next={cursor.Current.Kind}",
                _ => throw new InvalidOperationException($"Unknown cursor op '{op}'."),
            };
        }
        catch (GrammarException ex) {
            return $"ERR {Escape(ex.Detail)} @{ex.Line}:{ex.Column}";
        }

        string Ok(DslToken t) => $"OK {t.Kind} [{Escape(t.Text)}] next={cursor.Current.Kind}";
    }

    private static string Digest(List<string> dump) =>
        $"{dump.Count}\t{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", dump))))[..16]}";

    // ── escaping: reversible, and every invisible character is written as \uXXXX ──────

    internal static string Escape(string s) {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s) {
            switch (ch) {
                case '\\': sb.Append("\\\\"); break;
                case '\t': sb.Append("\\t"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                default:
                    if (ch < ' ' || ch > '~')
                        sb.Append("\\u").Append(((int)ch).ToString("X4", CultureInfo.InvariantCulture));
                    else
                        sb.Append(ch);
                    break;
            }
        }

        return sb.ToString();
    }

    internal static string Unescape(string s) {
        var sb = new StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i++) {
            if (s[i] != '\\') {
                sb.Append(s[i]);
                continue;
            }

            if (i + 1 >= s.Length)
                throw new InvalidOperationException($"Dangling escape in baseline field: {s}");
            var c = s[++i];
            if (c == 'u') {
                sb.Append((char)int.Parse(s.AsSpan(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                i += 4;
                continue;
            }

            sb.Append(c switch {
                't' => '\t',
                'n' => '\n',
                'r' => '\r',
                '\\' => '\\',
                _ => throw new InvalidOperationException($"Unknown escape '\\{c}' in baseline field: {s}"),
            });
        }

        return sb.ToString();
    }

    // The expression baseline's own escaping (ExprDifferentialTests): \t \n \r and any escaped character as itself.
    private static string UnescapeExprField(string s) {
        var sb = new StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i++) {
            if (s[i] != '\\' || i == s.Length - 1) {
                sb.Append(s[i]);
                continue;
            }

            i++;
            sb.Append(s[i] switch { 't' => '\t', 'n' => '\n', 'r' => '\r', var c => c });
        }

        return sb.ToString();
    }

    // ── records ───────────────────────────────────────────────────────────────────────

    /// <summary>One baseline record: its kind, its fields after the kind, and (for H) the token lines.</summary>
    private sealed record BaselineRecord(string Kind, string[] Fields, List<string> Body);

    private static string Root() => GuideBlocks.RepoRoot();

    /// <summary>The expressions of the frozen expression corpus: (kind, key, input), kind E for a text and X for a spec.</summary>
    private static List<(string Kind, string Key, string Input)> ExpressionCorpus() {
        var corpus = new List<(string, string, string)>();
        foreach (var line in File.ReadAllLines(Path.Combine(Root(), ExprBaselinePath))) {
            if (line.Length == 0 || line[0] == '#')
                continue;
            var fields = line.Split('\t');
            var key = UnescapeExprField(fields[1]);
            corpus.Add(fields[0] switch {
                "T" => ("E", key, key),
                "G" => ("X", key, ExprDifferentialTests.Generate(key)),
                _ => throw new InvalidOperationException($"Unknown expression baseline entry kind '{fields[0]}'."),
            });
        }

        return corpus;
    }

    /// <summary>Re-derives a record from its key with the CURRENT code; the result has the record's own shape.</summary>
    private static List<string> Derive(string kind, string[] keyFields) {
        switch (kind) {
            case "H": {
                    var input = Unescape(keyFields[0]);
                    return [$"H\t{Escape(input)}", .. TokenDump(input).Select(l => "\t" + l)];
                }
            case "U": {
                    var input = Unescape(keyFields[0]);
                    return [$"U\t{Escape(input)}\t{HeaderResult(input)}"];
                }
            case "C": {
                    var input = Unescape(keyFields[1]);
                    return [$"C\t{keyFields[0]}\t{Escape(input)}\t{CursorResult(keyFields[0], input)}"];
                }
            case "D": {
                    var path = Path.Combine(Root(), keyFields[0]);
                    if (!File.Exists(path))
                        throw new FileNotFoundException($"Golden domain '{keyFields[0]}' named by the token baseline is missing.", path);
                    return [$"D\t{keyFields[0]}\t{Digest(TokenDump(File.ReadAllText(path)))}"];
                }
            case "E": {
                    var input = Unescape(keyFields[0]);
                    return [$"E\t{Escape(input)}\t{Digest(TokenDump(input))}"];
                }
            case "X": {
                    var spec = Unescape(keyFields[0]);
                    return [$"X\t{Escape(spec)}\t{Digest(TokenDump(ExprDifferentialTests.Generate(spec)))}"];
                }
            default:
                throw new InvalidOperationException($"Unknown token baseline record kind '{kind}'.");
        }
    }

    private static List<BaselineRecord> ReadBaseline() {
        var records = new List<BaselineRecord>();
        foreach (var line in File.ReadAllLines(Path.Combine(Root(), BaselinePath))) {
            if (line.Length == 0 || line[0] == '#')
                continue;
            if (line[0] == '\t') {
                if (records.Count == 0 || records[^1].Kind != "H")
                    throw new InvalidOperationException($"Token line outside an H record: {line}");
                records[^1].Body.Add(line);
                continue;
            }

            var fields = line.Split('\t');
            records.Add(new BaselineRecord(fields[0], fields[1..], []));
        }

        return records;
    }

    // The number of leading fields that are the record's key (the rest is the frozen result).
    private static int KeyFieldCount(string kind) => kind == "C" ? 2 : 1;

    private static List<string> Lines(BaselineRecord record) =>
        [string.Join('\t', [record.Kind, .. record.Fields]), .. record.Body];

    // ── tests ─────────────────────────────────────────────────────────────────────────

    /// <summary>Writes the frozen baseline from the CURRENT tokenizer. Inert unless <c>POLY_REGEN_TOKEN_BASELINE</c> is set.</summary>
    [Test]
    public async Task Regenerate_WritesBaselineFromCurrentTokenizer() {
        if (Environment.GetEnvironmentVariable("POLY_REGEN_TOKEN_BASELINE") is null)
            return;

        var lines = new List<string> {
            "# G-12 frozen DSL token-stream baseline, written by the pre-span tokenizer. See DslTokenStreamBaselineTests.",
            "# H input / TAB kind start length line col text (or TAB ERR detail line col offset length); U header; C cursor;",
            "# D golden path, E expression text, X generator spec: token-line count and SHA-256 (16 hex) of the same dump.",
        };
        foreach (var input in HostileInputs())
            lines.AddRange(Derive("H", [Escape(input)]));
        foreach (var input in UsesHeaders)
            lines.AddRange(Derive("U", [Escape(input)]));
        foreach (var (op, input) in CursorCases)
            lines.AddRange(Derive("C", [op, Escape(input)]));
        foreach (var domain in GoldenDomains.Compilable)
            lines.AddRange(Derive("D", [domain.Path]));
        foreach (var (kind, key, _) in ExpressionCorpus())
            lines.AddRange(Derive(kind, [Escape(key)]));

        File.WriteAllLines(Path.Combine(Root(), BaselinePath), lines);
        await Assert.That(lines.Count).IsGreaterThan(1000);
    }

    [Test]
    public async Task Baseline_IsLargeAndCoversEveryTokenKind() {
        var records = ReadBaseline();
        var tokenLines = records.Where(r => r.Kind == "H").SelectMany(r => r.Body).ToList();
        var seenKinds = tokenLines
            .Select(l => l.Split('\t')[1])
            .Where(k => k != "ERR")
            .Select(Enum.Parse<DslTokenKind>)
            .ToHashSet();

        await Assert.That(records.Count(r => r.Kind == "H")).IsGreaterThan(80);
        await Assert.That(records.Count(r => r.Kind == "U")).IsEqualTo(UsesHeaders.Length);
        await Assert.That(records.Count(r => r.Kind == "C")).IsEqualTo(CursorCases.Length);
        await Assert.That(records.Count(r => r.Kind == "D")).IsEqualTo(GoldenDomains.Compilable.Count);
        await Assert.That(records.Count(r => r.Kind == "E")).IsGreaterThan(1500);
        await Assert.That(records.Count(r => r.Kind == "X")).IsGreaterThan(80);
        await Assert.That(tokenLines.Count).IsGreaterThan(350);
        await Assert.That(tokenLines.Count(l => l.StartsWith("\tERR\t", StringComparison.Ordinal))).IsGreaterThan(8);
        await Assert.That(seenKinds.SetEquals(Enum.GetValues<DslTokenKind>())).IsTrue();
        await Assert.That(records.Where(r => r.Kind is "D" or "E" or "X").Sum(r => int.Parse(r.Fields[1], CultureInfo.InvariantCulture)))
            .IsGreaterThan(20_000);
        await Assert.That(records.Select(r => (r.Kind, string.Join('\t', r.Fields.Take(KeyFieldCount(r.Kind))))).Distinct().Count())
            .IsEqualTo(records.Count);
    }

    [Test]
    public async Task Tokenizer_MatchesFrozenBaseline() {
        var failures = new List<string>();
        foreach (var record in ReadBaseline()) {
            var expected = Lines(record);
            var actual = Derive(record.Kind, record.Fields[..KeyFieldCount(record.Kind)]);
            if (!expected.SequenceEqual(actual, StringComparer.Ordinal))
                failures.Add($"{expected[0]}\n    expected {string.Join(" | ", expected)}\n    actual   {string.Join(" | ", actual)}");
        }

        await Assert.That(failures).IsEmpty();
    }
}