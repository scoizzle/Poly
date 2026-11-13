using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

using Poly.DomainModeling.Ontology;
using Poly.Grammar;
using Poly.Tests.Docs;

namespace Poly.Tests.DomainModeling.Parsing;

/// <summary>
/// G-10's preservation oracle: a frozen corpus of DSL expressions, each stored with what the
/// expression parser did with it before the iterative driver landed. The result is the canonical
/// print of the parsed tree (<see cref="Canonical"/>, this class's own printer, which covers
/// call, match and unresolved-duration nodes) or <c>ERR detail @line:col</c>. The baseline file is
/// DATA frozen from master's parser (<c>ExprDifferentialBaseline.txt</c>); it is not a second
/// parser. A line whose result moved is a failure unless <see cref="AcceptedDeltas"/> lists it with
/// the reason, so a behaviour change is always a reviewed decision.
///
/// <para>Corpus: every expression-shaped line of the guide's fences, the corpus <c>*.poly</c>
/// files and the parser test classes' string literals that parsed; a fixed-seed generator of 600
/// small expressions; depth-targeted generators around 255/256/257; hand-written precedence cases
/// (<c>not</c> before a comparison, <c>a + not b</c>, <c>is not</c> over <c>is (not x)</c>,
/// <c>path-cmp</c>, where bodies ending at <c>or</c>) and malformed inputs (unbalanced group, call and
/// match, empty <c>where</c> body, trailing operator, <c>(Price &gt; 3 4)</c>, <c>(… function)</c>,
/// <c>F()</c>).</para>
///
/// <para>Regeneration (<c>POLY_REGEN_EXPR_BASELINE=1</c>) rewrites the oracle from the CURRENT
/// parser. Never regenerate on a changed parser: that would move the oracle with the code.</para>
/// </summary>
public sealed class ExprDifferentialTests {
    private const string BaselinePath = "Poly.Tests/DomainModeling/Parsing/ExprDifferentialBaseline.txt";

    private static readonly Regex StringLiteral = new("\"((?:[^\"\\\\\\n]|\\\\\"|\\\\\\\\)*)\"", RegexOptions.Compiled);

    private static readonly string[] AtomPool = [
        "Flag", "Price", "Age", "Total", "Qty", "x", "y", "z", "Name", "Amount", "Ready", "42", "7", "0",
        "\"abc\"", "true", "false", "null", "3 Days", "12 Hours", "Order Price", "Items Product Name",
        "Kids exists", "count Kids", "sum Kids Price",
    ];

    private static readonly string[] CompareOps = [">", ">=", "<", "<=", "==", "!=", "is"];

    // ── the result of one expression ──────────────────────────────────────────────────

    /// <summary>What the parser does with <paramref name="input"/>: <c>OK</c> + canonical print, or <c>ERR</c>.</summary>
    internal static string Result(string input, bool digest) {
        try {
            var parsed = DslExpressionFragment.ParseExpressionFragment(input);
            var text = Canonical(parsed);
            return digest ? $"OK len={text.Length} sha={Sha(text)}" : "OK " + text;
        }
        catch (GrammarException ex) {
            return $"ERR {ex.Detail} @{ex.Line}:{ex.Column}";
        }
        catch (Exception ex) {
            return $"ERR! {ex.GetType().Name}: {ex.Message}";
        }
    }

    private static string Sha(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..12];

    private static readonly Dictionary<Type, PropertyInfo[]> PropertyCache = [];

    /// <summary>
    /// Canonical text of an expression tree: type name plus every declared property in order,
    /// recursing into expressions, arms and lists. Covers every node type, so a new node cannot
    /// slip through unprinted.
    /// </summary>
    internal static string Canonical(DomainExpression expression) {
        var sb = new StringBuilder();
        Print(expression, sb);
        return sb.ToString();
    }

    private static void Print(object? value, StringBuilder sb) {
        switch (value) {
            case null:
                sb.Append("null");
                return;
            case string s:
                sb.Append('"').Append(s).Append('"');
                return;
            case DomainExpression e:
                PrintNode(e, sb);
                return;
            case MatchArm arm:
                sb.Append("Arm(");
                Print(arm.Condition, sb);
                sb.Append(',');
                Print(arm.Value, sb);
                sb.Append(')');
                return;
            case System.Collections.IEnumerable list:
                sb.Append('[');
                var first = true;
                foreach (var item in list) {
                    if (!first) sb.Append(',');
                    first = false;
                    Print(item, sb);
                }
                sb.Append(']');
                return;
            case IFormattable f:
                sb.Append(value.GetType().Name).Append(':').Append(f.ToString(null, CultureInfo.InvariantCulture));
                return;
            default:
                sb.Append(value);
                return;
        }
    }

    private static void PrintNode(DomainExpression e, StringBuilder sb) {
        var type = e.GetType();
        if (!PropertyCache.TryGetValue(type, out var props)) {
            props = [.. type
                .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(p => p.GetIndexParameters().Length == 0 && p.Name is not ("Children" or "EqualityContract"))];
            lock (PropertyCache)
                PropertyCache[type] = props;
        }

        sb.Append(type.Name).Append('(');
        for (var i = 0; i < props.Length; i++) {
            if (i > 0) sb.Append(',');
            Print(props[i].GetValue(e), sb);
        }
        sb.Append(')');
    }

    // ── depth-targeted and shape generators (specs are stored, inputs regenerate) ─────

    private static string Repeat(string unit, int count, string separator) =>
        string.Join(separator, Enumerable.Repeat(unit, count));

    /// <summary>Right-nested chain: <c>Flag op (Flag op (… Flag))</c> with <paramref name="levels"/> operators.</summary>
    private static string RightNested(string op, int levels) {
        var body = "Flag";
        for (var i = 0; i < levels; i++)
            body = $"Flag {op} ({body})";
        return body;
    }

    private static string CompareNested(int levels) {
        var body = "Flag";
        for (var i = 0; i < levels; i++)
            body = $"Flag > ({body})";
        return body;
    }

    private static string IsNotNested(int levels) {
        var body = "Flag";
        for (var i = 0; i < levels; i++)
            body = $"Flag is not ({body})";
        return body;
    }

    private static string WhereNested(int levels) {
        var body = "Flag";
        for (var i = 0; i < levels; i++)
            body = $"Kids where (Flag and {body})";
        return body;
    }

    internal static string Generate(string spec) {
        var parts = spec.Split(':');
        var count = int.Parse(parts[^1], CultureInfo.InvariantCulture);
        return parts[0] switch {
            "chain" => Repeat("Flag", count, $" {parts[1]} "),
            "right" => RightNested(parts[1], count),
            "cmp-left" => Repeat("Flag", count, " + ") + " > Flag",
            "cmp-right" => "Flag > " + Repeat("Flag", count, " + "),
            "cmp-both" => Repeat("Flag", count, " + ") + " > " + Repeat("Flag", count, " + "),
            "cmp-nest" => CompareNested(count),
            "isnot-nest" => IsNotNested(count),
            "parens" => ParserLimitsTests.Parens(count),
            "nots" => ParserLimitsTests.Nots(count),
            "calls" => ParserLimitsTests.Calls(count),
            "matches" => ParserLimitsTests.Matches(count),
            "mixed" => ParserLimitsTests.Mixed(count),
            "where-nest" => WhereNested(count),
            "not-add" => "not " + Repeat("Flag", count, " + "),
            "and-of-nots" => Repeat("not Flag", count, " and "),
            _ => throw new InvalidOperationException($"Unknown generator spec '{spec}'."),
        };
    }

    private static IEnumerable<string> DepthSpecs() {
        foreach (var op in new[] { "and", "or", "+", "*" }) {
            foreach (var n in new[] { 255, 256, 257, 258, 259 })
                yield return $"chain:{op}:{n}";
            foreach (var n in new[] { 126, 127, 128, 129, 130 })
                yield return $"right:{op}:{n}";
        }

        foreach (var op in new[] { "-", "/" }) {
            foreach (var n in new[] { 256, 257, 258 })
                yield return $"chain:{op}:{n}";
            foreach (var n in new[] { 127, 128, 129 })
                yield return $"right:{op}:{n}";
        }

        foreach (var n in new[] { 255, 256, 257, 258 }) {
            yield return $"cmp-left:{n}";
            yield return $"cmp-right:{n}";
        }

        foreach (var n in new[] { 128, 129, 130 })
            yield return $"cmp-both:{n}";

        foreach (var n in new[] { 126, 127, 128, 129, 130 }) {
            yield return $"cmp-nest:{n}";
            yield return $"isnot-nest:{n}";
        }

        foreach (var n in new[] { 255, 256, 257, 258 }) {
            yield return $"parens:{n}";
            yield return $"nots:{n}";
            yield return $"calls:{n}";
            yield return $"matches:{n}";
            yield return $"mixed:{n}";
            yield return $"not-add:{n}";
        }

        foreach (var n in new[] { 84, 85, 86, 127, 128, 129 })
            yield return $"where-nest:{n}";
        foreach (var n in new[] { 256, 257, 258 })
            yield return $"and-of-nots:{n}";
    }

    // ── hand-written shape and malformed cases ────────────────────────────────────────

    private static readonly string[] HandWritten = [
        // precedence and associativity
        "a or b and c", "a and b or c and d", "a - b - c", "a / b / c", "a + b * c - d / e", "a * b + c * d",
        "a < b", "a is b", "a is not b", "a is not b + c", "a + b is not c * d", "a is (not b)",
        "not a", "not a and b", "a and not b", "a or not b and c", "not not a", "not not not a",
        "not a + b", "not a * b", "not (a > b)", "a + not b", "a + not b + c", "a * not b", "a * not b * c",
        "a + not b > c", "a > not b", "a > not b + c", "a is not not b", "a is not b is not c",
        "not a is b", "not a > b", "a and not b > c", "(not a) > b", "not (a) > b",
        "a > b and c < d", "a > b or c < d and e is f", "a < b and c", "a and b < c", "a + 1 > b - 2 * 3",
        "x - 1 - 2 - 3", "a and (b or c) and d", "(a or b) and (c or d)", "((a))", "(((a + b) * c) - d)",
        // atoms
        "42", "\"text\"", "true", "false", "null", "Flag", "3 Days", "12 Hours", "3 Days + 4 Hours",
        "Order Price", "Order Price Name", "Order Price > 3", "Order Price is not 3", "Order Price > 3 + 4",
        "Order Price > (a + b)", "Order Price > not a", "Order Price > not a + b", "Order Price > F(1)",
        "Order Price > Kids where Flag", "Order Price > Kids where Flag and Ready",
        "Order Price == Items Qty", "Order Price != 4 and Flag", "Order Price is not Items Qty and Flag",
        "Kids exists", "Kids exists and Flag", "not Kids exists", "count Kids", "count Kids > 2",
        "sum Kids Price", "sum Kids Price > 100", "sum Kids Price + 1",
        // quantifiers and where
        "Kids where Flag", "Kids where Flag and Ready", "Kids where Flag or Ready", "Kids where Price > 3",
        "Kids where Price > 3 and Qty < 2", "Kids where not Flag", "Kids where (Flag or Ready) and Qty",
        "Kids where (Kids where Flag)", "Kids where F(Kids where Flag)", "Kids where match { Kids where Flag -> 1, else -> 2 } > 0",
        "any Kids where Flag", "all Kids where Flag and Ready", "none Kids where Price > 3", "count Kids where Flag",
        "count Kids where Flag > 1", "any Kids where Flag or Ready", "Flag and any Kids where Ready",
        "Kids where Flag and Ready or Total > 4", "any Kids where Order Price > 3", "any Kids where Order Price is not 3",
        // calls and matches
        "F()", "F(1)", "F(1, 2)", "F(a + b, c and d)", "F(F(1), G(2, 3))", "F(a, (b, c))",
        "F(not a)", "F(a > b)", "F(Kids where Flag)", "F(any Kids where Flag, 2)", "F(match { a -> 1, else -> 2 })",
        "F(match { a -> 1, b -> 2, else -> 3 }, 4)", "F(a) + G(b)", "F(a) > G(b)", "not F(a)", "F(a) and G(b)",
        "match { else -> 1 }", "match { a -> 1, else -> 2 }", "match { a -> 1, b -> 2, else -> 3 }",
        "match { a > 1 and b -> x + 1, else -> y * 2 }", "match { F(a, b) -> G(c), else -> H(d) }",
        "match { a -> match { b -> 1, else -> 2 }, else -> 3 }", "match { (a) -> (1), else -> (2) }",
        "match { a -> 1, else -> 2 } + 1", "1 + match { a -> 1, else -> 2 }", "match { a -> 1, else -> 2 } > 0",
        "match { not a -> 1, else -> 2 }", "match { a is not b -> 1, else -> 2 }",
        "match { Kids where Flag -> 1, else -> 2 }", "match { a -> Kids where Flag, else -> 2 }",
        "match", "match + 1", "match > 1", "match and Flag",
        // identifiers that look like keywords
        "where", "exists", "count", "sum", "any", "function", "value", "Kids where", "count Kids where",
        // durations
        "3 Days > 2 Days", "Now + 3 Days", "a + 3 Days", "3 Days + a", "(3 Days)", "F(3 Days)", "F(3 Days, 4 Hours)",
        "match { a -> 3 Days, else -> 4 Hours }", "3", "3 + 4", "3 4", "3 days", "3 DAYS",
        // negative and unusual literals
        "\"a\" + \"b\"", "\"\"", "null is null", "true and false", "true is not false",
        // malformed: groups
        "(", ")", "()", "(())", "(a", "a)", "((a)", "(a))", "(a b)", "(a +)", "(+ a)", "(a > )", "(a and)",
        "(Price > 3 4)", "(Price > 3 function)", "(Flag function)", "(function)", "(3 function)", "(3 Foo)",
        "(3 Foo Bar)", "(a b c)", "(Kids where)", "(not)", "(a,)", "(a, b)", "(a;)",
        // malformed: calls
        "F(", "F(a", "F(a,", "F(a,)", "F(,a)", "F(a,,b)", "F(,)", "F(a b)", "F(1 x)", "F(3 function)", "F(a))",
        "F(a ]", "F(a] )", "F(()", "F((a)", "F(a, (b)", "F(not)", "F(a +)", "F(a >)", "F(1 Days x)",
        "F(match { a -> 1, else -> 2 }", "F(match { a -> 1 )", "F(Kids where)", "F(Kids where) ", "f ( a )",
        // malformed: matches
        "match {", "match { }", "match { a", "match { a -> 1", "match { a -> 1, else -> 2", "match { else }",
        "match { else -> }", "match { -> 1, else -> 2 }", "match { a -> , else -> 2 }", "match { a 1, else -> 2 }",
        "match { a -> 1 }", "match { a -> 1, }", "match { , a -> 1, else -> 2 }", "match { a -> 1,, else -> 2 }",
        "match { else -> 1, else -> 2 }", "match { else -> 1, a -> 2 }", "match { a else -> 1, else -> 2 }",
        "match { a -> 1 2, else -> 3 }", "match { a b -> 1, else -> 2 }", "match { a -> 1, else -> 2 3 }",
        "match { a -> 1, else -> 2, else -> 3 }", "match { a -> else, else -> 2 }", "match { (else) -> 1, else -> 2 }",
        "match { a -> 1, else -> 2 } 3", "match { a -> 1, else -> 2 } }", "match { a -> (1, else -> 2 }",
        "match { a -> 1 Days x, else -> 2 }", "match { 3 Foo -> 1, else -> 2 }", "match { a -> 3 Foo, else -> 2 }",
        "match { a -> 3 function, else -> 2 }", "match { a - -> 1, else -> 2 }", "match { a -> -, else -> 2 }",
        "match { a -> -1, else -> 2 }", "match { -1 -> 1, else -> 2 }", "match { a == -1 -> 1, else -> 2 }",
        // malformed: where and quantifiers
        "Kids where", "any Kids where", "all Kids where", "none Kids where", "count Kids where", "sum Kids where Flag",
        "sum Kids where", "Kids where Kids where Flag", "any Kids where all Kids where Flag", "Kids where Flag and Kids where Ready",
        "Kids where any Kids where Flag", "Kids where (any Kids where Flag)", "Kids where (Kids where (Kids where Flag))",
        "Kids where Flag Ready", "any Kids", "any", "all", "none", "sum", "sum Kids", "count Kids Price", "Kids where )",
        "Kids where (", "Kids where (Flag", "any Kids where (Flag", "Kids where Flag)", "Kids where 3 Foo",
        // malformed: operators and operands
        "a and", "a or", "a +", "a -", "a *", "a /", "+ a", "* a", "and a", "or a", "a and and b", "a + + b", "a * * b",
        "a >", "> a", "a > >", "a > > b", "a < b < c", "a > b > c", "a is b is c", "a == b != c", "a is not", "a is", "a is not not",
        "not", "not not", "not >", "not a >", "not a >= b >= c", "a not b", "a b", "a b c", "a b c d", "a 3", "3 a", "\"s\" a",
        "a, b", "a ; b", "a }", "a ]", "{ a }", "[a]", "a.b", "a : b", "a -> b", "a = b", "a => b",
        "Order Price >", "Order Price is not", "Order Price > )", "Order Price is not )", "Order Price > > 3", "Order Price > 3 > 4",
        "Order Price is not 3 4", "Order Price > (", "Order Price > (3", "Order Price > match { else -> 1", "Order Price > F(",
        "Order Price > not", "Order Price > Kids where", "Order Price is not Kids where", "Order Price > any Kids where",
        "a b >", "a b is not", "a b > 3 4", "a b > 3 Foo", "a b is not 3 Foo",
        // duration near-misses
        "3 Foo", "3 Foo Bar", "3 Days Foo", "3 Hours 4", "3 function", "3 Days function", "3 x y", "3 Foo > 2", "a > 3 Foo",
        "a + 3 Foo", "3 Foo + a", "(a + 3 Foo)", "F(3 Foo)", "F(a, 3 Foo)", "match { a -> 3 Foo, else -> 2 }",
        "Flag and 3 Foo", "Flag or 3 Foo", "not 3 Foo", "Kids where 3 Foo", "Kids where Flag and 3 Foo",
        "count Kids where Price > 3 Foo", "3 Days x", "3 Days Days", "3 Days 4 Days", "99999999999999999999 Days",
        "99999999999999999999",
        // empty and whitespace-only inputs are rejected before the parser; keep a trailing-token probe
        "Flag Flag", "Flag 1", "1 Flag", "1 Days 1", "Flag )", "Flag ,", "Flag }",
    ];

    // ── seeded valid-expression generator ─────────────────────────────────────────────

    private static string Or(Random r, int depth) => Chain(r, depth, " or ", And);
    private static string And(Random r, int depth) => Chain(r, depth, " and ", NotE);

    private static string Chain(Random r, int depth, string op, Func<Random, int, string> operand) {
        var n = r.Next(5) == 0 ? 3 : r.Next(3) == 0 ? 2 : 1;
        return string.Join(op, Enumerable.Range(0, n).Select(_ => operand(r, depth)));
    }

    private static string NotE(Random r, int depth) =>
        r.Next(6) == 0 ? "not " + Add(r, depth) : Cmp(r, depth);

    private static string Cmp(Random r, int depth) {
        var left = Add(r, depth);
        return r.Next(3) switch {
            0 => $"{left} {CompareOps[r.Next(CompareOps.Length)]} {Add(r, depth)}",
            1 when r.Next(4) == 0 => $"{left} is not {Add(r, depth)}",
            _ => left,
        };
    }

    private static string Add(Random r, int depth) {
        var n = r.Next(4) == 0 ? 2 : 1;
        var sb = new StringBuilder(Mul(r, depth));
        for (var i = 1; i < n; i++)
            sb.Append(r.Next(2) == 0 ? " + " : " - ").Append(Mul(r, depth));
        return sb.ToString();
    }

    private static string Mul(Random r, int depth) {
        var n = r.Next(5) == 0 ? 2 : 1;
        var sb = new StringBuilder(Primary(r, depth));
        for (var i = 1; i < n; i++)
            sb.Append(r.Next(2) == 0 ? " * " : " / ").Append(Primary(r, depth));
        return sb.ToString();
    }

    private static string Primary(Random r, int depth) {
        if (depth <= 0 || r.Next(3) != 0)
            return AtomPool[r.Next(AtomPool.Length)];
        return r.Next(8) switch {
            0 => $"({Or(r, depth - 1)})",
            1 => $"F({Or(r, depth - 1)}, {Or(r, depth - 1)})",
            2 => $"G({Or(r, depth - 1)})",
            3 => $"match {{ {Or(r, depth - 1)} -> {Or(r, depth - 1)}, else -> {Or(r, depth - 1)} }}",
            4 => $"Kids where {And(r, depth - 1)}",
            5 => $"{new[] { "any", "all", "none", "count" }[r.Next(4)]} Kids where {And(r, depth - 1)}",
            6 => $"Order Price {CompareOps[r.Next(CompareOps.Length)]} {AtomPool[r.Next(AtomPool.Length)]}",
            _ => $"not {Add(r, depth - 1)}",
        };
    }

    // ── corpus extraction (regeneration only) ─────────────────────────────────────────

    private static IEnumerable<string> LineCandidates(string text) {
        foreach (var raw in text.Split('\n')) {
            var line = raw.Trim();
            var comment = line.IndexOf("//", StringComparison.Ordinal);
            if (comment >= 0)
                line = line[..comment].Trim();
            if (line.Length == 0)
                continue;
            yield return line;
            foreach (var pattern in new[] { @"\brequire\s+(.*)", @"\bwhen\s+(.*)", @"\bpolicy\s*\{\s*(.*?)\s*\}?$", @"=\s*(.*)", @"\bif\s*\((.*)\)", @"\bdefault\s+(.*)" }) {
                var m = Regex.Match(line, pattern);
                if (m.Success && m.Groups[1].Value.Trim().Length > 0)
                    yield return m.Groups[1].Value.Trim().TrimEnd('}', '{').Trim();
            }
        }
    }

    private static IEnumerable<string> ExtractedCandidates(string root) {
        var guide = File.ReadAllText(Path.Combine(root, "Poly.Mcp", "Docs", "poly-dsl-guide.md"));
        foreach (var c in LineCandidates(guide))
            yield return c;

        foreach (var file in Directory.EnumerateFiles(root, "*.poly", SearchOption.AllDirectories)) {
            var rel = Path.GetRelativePath(root, file);
            if (rel.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                || rel.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                || rel.StartsWith(".git", StringComparison.Ordinal))
                continue;
            foreach (var c in LineCandidates(File.ReadAllText(file)))
                yield return c;
        }

        foreach (var dir in new[] { "Poly.Tests/DomainModeling/Parsing", "Poly.Tests/Grammar", "Poly.Tests/DomainModeling/Language", "Poly.Tests/Syntax" }) {
            var full = Path.Combine(root, dir);
            if (!Directory.Exists(full))
                continue;
            foreach (var file in Directory.EnumerateFiles(full, "*.cs", SearchOption.TopDirectoryOnly)) {
                if (Path.GetFileName(file) == nameof(ExprDifferentialTests) + ".cs")
                    continue;
                foreach (Match m in StringLiteral.Matches(File.ReadAllText(file))) {
                    var s = m.Groups[1].Value.Replace("\\\"", "\"").Replace("\\\\", "\\");
                    if (s.Length is > 0 and <= 240 && !s.Contains('\\'))
                        yield return s;
                }
            }
        }
    }

    // ── baseline file ─────────────────────────────────────────────────────────────────

    private static string Escape(string s) =>
        s.Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\n", "\\n").Replace("\r", "\\r");

    private static string Unescape(string s) {
        var sb = new StringBuilder();
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

    private sealed record Entry(string Key, string Input, bool Generated, string Result);

    private static List<Entry> ReadBaseline() {
        var path = Path.Combine(GuideBlocks.RepoRoot(), BaselinePath);
        var entries = new List<Entry>();
        foreach (var line in File.ReadAllLines(path)) {
            if (line.Length == 0 || line[0] == '#')
                continue;
            var fields = line.Split('\t');
            if (fields.Length != 3)
                throw new InvalidOperationException($"Baseline line is not 'kind<TAB>key<TAB>result': {line}");
            var key = Unescape(fields[1]);
            entries.Add(fields[0] switch {
                "T" => new Entry(key, key, false, Unescape(fields[2])),
                "G" => new Entry(key, Generate(key), true, Unescape(fields[2])),
                _ => throw new InvalidOperationException($"Unknown baseline entry kind '{fields[0]}'."),
            });
        }

        return entries;
    }

    private static void OnBigStack(System.Action body) {
        Exception? failure = null;
        var thread = new Thread(() => {
            try {
                body();
            }
            catch (Exception ex) {
                failure = ex;
            }
        }, 256 * 1024 * 1024);
        thread.Start();
        thread.Join();
        if (failure is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    /// <summary>
    /// Writes the frozen baseline from the CURRENT parser. Inert unless
    /// <c>POLY_REGEN_EXPR_BASELINE</c> is set. Run it on master's parser only.
    /// </summary>
    [Test]
    public async Task Regenerate_WritesBaselineFromCurrentParser() {
        if (Environment.GetEnvironmentVariable("POLY_REGEN_EXPR_BASELINE") is null)
            return;

        var lines = new List<string> {
            "# G-10 frozen expression baseline. kind<TAB>key<TAB>result. T = the text is the key, G = a generator spec.",
            "# Result: 'OK <canonical print>' (G entries: 'OK len= sha=') or 'ERR detail @line:col'. See ExprDifferentialTests.",
        };
        OnBigStack(() => {
            var root = GuideBlocks.RepoRoot();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            void AddText(string text, bool keepErrors) {
                if (text.Length == 0 || string.IsNullOrWhiteSpace(text) || !seen.Add(text))
                    return;
                var result = Result(text, digest: false);
                if (!keepErrors && !result.StartsWith("OK ", StringComparison.Ordinal)) {
                    seen.Remove(text);
                    return;
                }

                lines.Add($"T\t{Escape(text)}\t{Escape(result)}");
            }

            foreach (var text in HandWritten)
                AddText(text, keepErrors: true);

            var random = new Random(20261112);
            for (var attempts = 0; seen.Count < HandWritten.Length + 600 && attempts < 50_000; attempts++) {
                var generated = Or(random, 2);
                if (generated.Length <= 110)
                    AddText(generated, keepErrors: false);
            }

            foreach (var text in ExtractedCandidates(root).Where(t => t.Length <= 160).OrderBy(s => s, StringComparer.Ordinal))
                AddText(text, keepErrors: false);

            foreach (var spec in DepthSpecs())
                lines.Add($"G\t{Escape(spec)}\t{Escape(Result(Generate(spec), digest: true))}");
        });

        File.WriteAllLines(Path.Combine(GuideBlocks.RepoRoot(), BaselinePath), lines);
        await Assert.That(lines.Count).IsGreaterThan(100);
    }

    // ── the oracle ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Results that moved on purpose: <c>Key</c> (the input text or generator spec), the new result,
    /// and why. Categories: A the input now parses (match with commas in a call argument); B a malformed input the old fallback reported as trailing tokens or at a stale position is now reported where the piece is missing; C same message, error position moved to where it is found; D same depth message, position at the first crossing token.
    /// </summary>

    private const string R_A = "a match inside a call argument now parses: arguments end at their own comma or parenthesis in the stream, no longer pre-split at parenthesis depth (the old pre-split cut the match at its first comma)";
    private const string R_B = "a construct the old longest-match could not complete fell back (to a shorter atom, then trailing tokens or a stale position); the driver commits to the header and reports where the missing piece is";
    private const string R_C = "same message; fold-time errors are positioned at the cursor when they are found (the offending construct) instead of after the whole expression";
    private const string R_D = "same message; the depth error is raised at the first token whose level exceeds the limit (the 257th operator of a chain, the first crossing node) instead of at the chain's last operator or the left operand's first token";

    private static readonly (string Key, string Result, string Reason)[] AcceptedDeltas = [
        ("F(match { a -> 1, else -> 2 })", "OK FunctionCall(\"F\",[MatchExpr([Arm(PropertyAccess(\"a\"),Literal(Int64:1,null,Int32:1,Int32:16))],Literal(Int64:2,null,Int32:1,Int32:27),Int32:1,Int32:3)],Int32:1,Int32:1)", R_A),
        ("F(match { a -> 1, b -> 2, else -> 3 }, 4)", "OK FunctionCall(\"F\",[MatchExpr([Arm(PropertyAccess(\"a\"),Literal(Int64:1,null,Int32:1,Int32:16)),Arm(PropertyAccess(\"b\"),Literal(Int64:2,null,Int32:1,Int32:24))],Literal(Int64:3,null,Int32:1,Int32:35),Int32:1,Int32:3),Literal(Int64:4,null,Int32:1,Int32:40)],Int32:1,Int32:1)", R_A),
        ("Kids where", "ERR Expected expression, got 'end of input' @1:11", R_B),
        ("count Kids where", "ERR Expected expression, got 'end of input' @1:17", R_B),
        ("(", "ERR Expected expression, got 'end of input' @1:2", R_B),
        ("()", "ERR Expected expression inside parentheses @1:2", R_C),
        ("(a", "ERR Expected ')' to close the group @1:3", R_B),
        ("((a)", "ERR Expected ')' to close the group @1:5", R_B),
        ("(a +)", "ERR Expected expression, got ')' @1:5", R_B),
        ("(a > )", "ERR Expected expression, got ')' @1:6", R_B),
        ("(a and)", "ERR Expected expression, got ')' @1:7", R_B),
        ("(Kids where)", "ERR Expected expression, got ')' @1:12", R_B),
        ("(not)", "ERR Expected expression, got ')' @1:5", R_B),
        ("F(a,)", "ERR Expected an argument in call to 'F' before ')' (line 1, col 5) @1:5", R_C),
        ("F(,a)", "ERR Expected an argument in call to 'F' before ',' (line 1, col 3) @1:3", R_C),
        ("F(a,,b)", "ERR Expected an argument in call to 'F' before ',' (line 1, col 5) @1:5", R_C),
        ("F(,)", "ERR Expected an argument in call to 'F' before ',' (line 1, col 3) @1:3", R_C),
        ("F(not)", "ERR Expected expression, got ')' @1:6", R_B),
        ("F(a +)", "ERR Expected expression, got ')' @1:6", R_B),
        ("F(a >)", "ERR Expected expression, got ')' @1:6", R_B),
        ("F(Kids where)", "ERR Expected expression, got ')' @1:13", R_B),
        ("F(Kids where) ", "ERR Expected expression, got ')' @1:13", R_B),
        ("match { }", "ERR 'match' needs at least an 'else' arm (else -> value) (line 1, col 1) @1:1", R_C),
        ("match { else }", "ERR Expected '->' in 'match' arm starting at 'else' (line 1, col 9) @1:9", R_C),
        ("match { else -> }", "ERR Expected a value after '->' in 'match' (line 1, col 14) @1:14", R_C),
        ("match { -> 1, else -> 2 }", "ERR Expected a condition before '->' in 'match' (line 1, col 9) @1:9", R_C),
        ("match { a -> , else -> 2 }", "ERR Expected a value after '->' in 'match' (line 1, col 11) @1:11", R_C),
        ("match { a 1, else -> 2 }", "ERR Expected '->' in 'match' arm starting at 'a' (line 1, col 9) @1:9", R_C),
        ("match { a -> 1 }", "ERR 'match' requires a final 'else' arm (else -> value) (line 1, col 1) @1:1", R_C),
        ("match { a -> 1, }", "ERR Expected an arm in 'match' after the trailing ',' (line 1, col 17) @1:17", R_C),
        ("match { , a -> 1, else -> 2 }", "ERR Expected an arm in 'match' before ',' (line 1, col 9) @1:9", R_C),
        ("match { a -> 1,, else -> 2 }", "ERR Expected an arm in 'match' before ',' (line 1, col 16) @1:16", R_C),
        ("match { else -> 1, else -> 2 }", "ERR 'match' has more than one 'else' arm (line 1, col 20) @1:20", R_C),
        ("match { else -> 1, a -> 2 }", "ERR 'else' must be the last arm of a 'match' (line 1, col 9) @1:9", R_C),
        ("match { a else -> 1, else -> 2 }", "ERR 'else' cannot appear inside a 'match' condition (line 1, col 9) @1:9", R_C),
        ("match { a -> 1, else -> 2, else -> 3 }", "ERR 'match' has more than one 'else' arm (line 1, col 28) @1:28", R_C),
        ("match { (else) -> 1, else -> 2 }", "ERR 'else' cannot appear inside a 'match' condition (line 1, col 9) @1:9", R_C),
        ("match { a -> (1, else -> 2 }", "ERR Unexpected ',' in group @1:16", R_B),
        ("match { a - -> 1, else -> 2 }", "ERR Expected expression, got '->' @1:13", R_B),
        ("match { a == -1 -> 1, else -> 2 }", "ERR Expected expression, got '-' @1:14", R_B),
        ("any Kids where", "ERR Expected expression, got 'end of input' @1:15", R_B),
        ("all Kids where", "ERR Expected expression, got 'end of input' @1:15", R_B),
        ("none Kids where", "ERR Expected expression, got 'end of input' @1:16", R_B),
        ("sum Kids where", "ERR Expected expression, got 'end of input' @1:15", R_B),
        ("Kids where Kids where Flag", "ERR Nested 'where' is not allowed. Use parentheses for grouped conditions instead. @1:12", R_C),
        ("any Kids where all Kids where Flag", "ERR Nested 'where' is not allowed. Use parentheses for grouped conditions instead. @1:16", R_C),
        ("Kids where Flag and Kids where Ready", "ERR Nested 'where' is not allowed. Use parentheses for grouped conditions instead. @1:21", R_C),
        ("Kids where any Kids where Flag", "ERR Nested 'where' is not allowed. Use parentheses for grouped conditions instead. @1:12", R_C),
        ("Kids where )", "ERR Expected expression, got ')' @1:12", R_B),
        ("Kids where (", "ERR Expected expression, got 'end of input' @1:13", R_B),
        ("Kids where (Flag", "ERR Expected ')' to close the group @1:17", R_B),
        ("any Kids where (Flag", "ERR Expected ')' to close the group @1:21", R_B),
        ("a and", "ERR Expected expression, got 'end of input' @1:6", R_B),
        ("a or", "ERR Expected expression, got 'end of input' @1:5", R_B),
        ("a +", "ERR Expected expression, got 'end of input' @1:4", R_B),
        ("a -", "ERR Expected expression, got 'end of input' @1:4", R_B),
        ("a *", "ERR Expected expression, got 'end of input' @1:4", R_B),
        ("a /", "ERR Expected expression, got 'end of input' @1:4", R_B),
        ("a and and b", "ERR Expected expression, got 'and' @1:7", R_B),
        ("a + + b", "ERR Expected expression, got '+' @1:5", R_B),
        ("a * * b", "ERR Expected expression, got '*' @1:5", R_B),
        ("a >", "ERR Expected expression, got 'end of input' @1:4", R_B),
        ("a > >", "ERR Expected expression, got '>' @1:5", R_B),
        ("a > > b", "ERR Expected expression, got '>' @1:5", R_B),
        ("a is not", "ERR Expected expression, got 'end of input' @1:9", R_B),
        ("a is", "ERR Expected expression, got 'end of input' @1:5", R_B),
        ("a is not not", "ERR Expected expression, got 'end of input' @1:13", R_B),
        ("not", "ERR Expected expression, got 'end of input' @1:4", R_B),
        ("not not", "ERR Expected expression, got 'end of input' @1:8", R_B),
        ("not >", "ERR Expected expression, got '>' @1:5", R_B),
        ("Order Price >", "ERR Expected expression, got 'end of input' @1:14", R_B),
        ("Order Price is not", "ERR Expected expression, got 'end of input' @1:19", R_B),
        ("Order Price > )", "ERR Expected expression, got ')' @1:15", R_B),
        ("Order Price is not )", "ERR Expected expression, got ')' @1:20", R_B),
        ("Order Price > > 3", "ERR Expected expression, got '>' @1:15", R_B),
        ("Order Price > (", "ERR Expected expression, got 'end of input' @1:16", R_B),
        ("Order Price > (3", "ERR Expected ')' to close the group @1:17", R_B),
        ("Order Price > not", "ERR Expected expression, got 'end of input' @1:18", R_B),
        ("Order Price > Kids where", "ERR Expected expression, got 'end of input' @1:25", R_B),
        ("Order Price is not Kids where", "ERR Expected expression, got 'end of input' @1:30", R_B),
        ("Order Price > any Kids where", "ERR Expected expression, got 'end of input' @1:29", R_B),
        ("a b >", "ERR Expected expression, got 'end of input' @1:6", R_B),
        ("a b is not", "ERR Expected expression, got 'end of input' @1:11", R_B),
        ("99999999999999999999 Days", "ERR Duration amount '99999999999999999999' is not an integer. @1:1", R_C),
        ("chain:and:259", "ERR expression nesting exceeds the limit of 256 @1:2310", R_D),
        ("chain:or:259", "ERR expression nesting exceeds the limit of 256 @1:2054", R_D),
        ("chain:+:259", "ERR expression nesting exceeds the limit of 256 @1:1798", R_D),
        ("chain:*:259", "ERR expression nesting exceeds the limit of 256 @1:1798", R_D),
        ("cmp-left:257", "ERR expression nesting exceeds the limit of 256 @1:1798", R_D),
        ("cmp-right:258", "ERR expression nesting exceeds the limit of 256 @1:1798", R_D),
        ("cmp-nest:129", "ERR expression nesting exceeds the limit of 256 @1:1030", R_D),
        ("isnot-nest:129", "ERR expression nesting exceeds the limit of 256 @1:1670", R_D),
        ("cmp-nest:130", "ERR expression nesting exceeds the limit of 256 @1:1030", R_D),
        ("isnot-nest:130", "ERR expression nesting exceeds the limit of 256 @1:1670", R_D),
        ("mixed:257", "ERR expression nesting exceeds the limit of 256 @1:1284", R_D),
        ("mixed:258", "ERR expression nesting exceeds the limit of 256 @1:1275", R_D),
        ("not-add:258", "ERR expression nesting exceeds the limit of 256 @1:1795", R_D),
        ("and-of-nots:257", "ERR expression nesting exceeds the limit of 256 @1:3325", R_D),
        ("and-of-nots:258", "ERR expression nesting exceeds the limit of 256 @1:3325", R_D),
    ];

    [Test]
    public async Task Baseline_IsLargeAndCoversEveryCorpusKind() {
        var entries = ReadBaseline();

        await Assert.That(entries.Count).IsGreaterThan(1000);
        await Assert.That(entries.Count(e => e.Generated)).IsGreaterThan(80);
        await Assert.That(entries.Count(e => e.Result.StartsWith("ERR", StringComparison.Ordinal))).IsGreaterThan(150);
        await Assert.That(entries.Select(e => e.Key).Distinct().Count()).IsEqualTo(entries.Count);
    }

    [Test]
    public async Task Parser_MatchesFrozenBaselineExceptListedDeltas() {
        var entries = ReadBaseline();
        var deltas = AcceptedDeltas.ToDictionary(d => d.Key, d => d, StringComparer.Ordinal);
        var failures = new List<string>();

        OnBigStack(() => {
            foreach (var entry in entries) {
                var actual = Result(entry.Input, digest: entry.Generated);
                var expected = deltas.TryGetValue(entry.Key, out var delta) ? delta.Result : entry.Result;
                if (!string.Equals(actual, expected, StringComparison.Ordinal))
                    failures.Add($"{Escape(entry.Key)}\n    expected {expected}\n    actual   {actual}");
            }
        });

        if (Environment.GetEnvironmentVariable("POLY_EXPR_DELTA_OUT") is { } dump)
            File.WriteAllLines(dump, failures.Select(f => f.Replace("\n", " | ")));
        await Assert.That(failures).IsEmpty();
    }

    [Test]
    public async Task AcceptedDeltas_AreLiveAndGiveTheirReason() {
        var entries = ReadBaseline().ToDictionary(e => e.Key, e => e, StringComparer.Ordinal);

        foreach (var (key, result, reason) in AcceptedDeltas) {
            await Assert.That(entries.ContainsKey(key)).IsTrue();
            await Assert.That(result).IsNotEqualTo(entries[key].Result);
            await Assert.That(reason).IsNotEmpty();
        }
    }
}