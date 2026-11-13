using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Language;
using Poly.Packs.Product;
using Poly.Tests.Docs;

namespace Poly.Tests.DomainModeling.Parsing;

internal enum ErrorSiteKind { Expect, ExpectIdentifier, Throw }

/// <summary>
/// One hand-written check or throw in a <c>DomainResolver*.cs</c> partial, found by a source scan.
/// <see cref="Id"/> is <c>partial:method#ordinal</c> (the ordinal counts every site of the method in source
/// order), never a line; <see cref="Line"/> and <see cref="StatementLine"/> are transient, used only to map
/// a stack frame back to the site.
/// </summary>
internal sealed record ErrorSite(
    string Partial,
    string Method,
    int Ordinal,
    ErrorSiteKind Kind,
    int Line,
    int StatementLine,
    string? MessagePattern,
    string? ExemptReason,
    bool ShowsCurrent) {
    public string Id => $"{Partial}:{Method}#{Ordinal}";

    /// <summary>
    /// The label an offending-token case belongs to, or null when the message does not show the offending token:
    /// <c>E:Kind</c> for an <c>Expect</c>, <c>EI:context</c> for an <c>ExpectIdentifier</c>, <c>T:site</c> for a throw that
    /// shows <c>Shown(Current)</c>.
    /// </summary>
    public string? OffenderLabel => Kind switch {
        ErrorSiteKind.Expect => "E:" + ExpectedKind,
        ErrorSiteKind.ExpectIdentifier => "EI:" + ExpectedKind,
        _ => ShowsCurrent ? "T:" + Id : null,
    };

    /// <summary>The expected token kind of an <c>Expect</c>, or the context string of an <c>ExpectIdentifier</c>.</summary>
    public string? ExpectedKind { get; init; }
}

/// <summary>
/// Where an exception came from: a scanned resolver site, a table row element (<c>table:rule/pattern#element</c>, the
/// first expected element of a failure <see cref="DslCursor.MatchOrThrow"/> reported, with every expected element and
/// refusal in <see cref="Labels"/>), or something that is neither.
/// </summary>
internal sealed record ErrorOrigin(string? SiteId, string Class, int Line) {
    public bool IsSite => SiteId is not null;

    /// <summary>For a table origin, every row element the failure named (label reach); empty otherwise.</summary>
    public IReadOnlyList<string> Labels { get; init; } = [];
}

/// <summary>
/// G-26b's site scanner and reach probe. The scanner reads the resolver partials with Roslyn (the test
/// project already references it) and lists the 91 <c>Expect</c>, 55 <c>ExpectIdentifier</c> and 81 <c>throw</c>
/// sites; the probe resolves a document, catches the exception and walks its stack: the first frame above
/// <see cref="DslCursor"/>'s helpers is the call site. The walk fails closed (see <see cref="AssertProbeIsSound"/>).
/// </summary>
internal static class ResolverErrorSiteScanner {
    private const string Directory = "Poly/DomainModeling/Language";

    private static List<ErrorSite>? _sites;

    public static IReadOnlyList<ErrorSite> Sites => _sites ??= Scan();

    public static string PartialOf(string path) {
        var name = Path.GetFileNameWithoutExtension(path);
        return name == "DomainResolver" ? "Core" : name["DomainResolver.".Length..];
    }

    private static List<ErrorSite> Scan() {
        var sites = new List<ErrorSite>();
        var folder = Path.Combine(GuideBlocks.RepoRoot(), Directory);
        foreach (var path in System.IO.Directory.GetFiles(folder, "DomainResolver*.cs").OrderBy(p => p, StringComparer.Ordinal))
            sites.AddRange(ScanSource(PartialOf(path), File.ReadAllText(path)));
        return sites;
    }

    /// <summary>The sites of one partial's source text (pure; the self-tests scan a mutated copy).</summary>
    public static List<ErrorSite> ScanSource(string partial, string source) {
        var sites = new List<ErrorSite>();
        var root = CSharpSyntaxTree.ParseText(source).GetRoot();
        var perMethod = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var node in root.DescendantNodes()) {
            var kind = Classify(node);
            if (kind is null)
                continue;
            var method = MethodOf(node);
            var ordinal = perMethod[method] = perMethod.GetValueOrDefault(method) + 1;
            var (pattern, exempt) = Message(node, kind.Value);
            sites.Add(new ErrorSite(partial, method, ordinal, kind.Value, LineOf(node), StatementLineOf(node), pattern, exempt, ShowsCurrentToken(node, kind.Value, pattern)) {
                ExpectedKind = ExpectedOf(node, kind.Value),
            });
        }

        return sites;
    }

    // A throw is offender-sensitive when its message is the `... got '<current token>'` shape.
    private static bool ShowsCurrentToken(SyntaxNode node, ErrorSiteKind kind, string? pattern) =>
        kind == ErrorSiteKind.Throw
        && node.ToString().Contains("Shown(Current)", StringComparison.Ordinal)
        && pattern is not null
        && pattern.Contains("got\\ '", StringComparison.Ordinal);

    private static string? ExpectedOf(SyntaxNode node, ErrorSiteKind kind) {
        if (node is not InvocationExpressionSyntax call)
            return null;
        return kind switch {
            ErrorSiteKind.Expect => call.ArgumentList.Arguments[0].Expression is MemberAccessExpressionSyntax m ? m.Name.Identifier.Text : null,
            ErrorSiteKind.ExpectIdentifier => call.ArgumentList.Arguments[1].Expression is LiteralExpressionSyntax l ? l.Token.ValueText : null,
            _ => null,
        };
    }

    private static ErrorSiteKind? Classify(SyntaxNode node) => node switch {
        InvocationExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.Text: "Expect" } } => ErrorSiteKind.Expect,
        InvocationExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.Text: "ExpectIdentifier" } } => ErrorSiteKind.ExpectIdentifier,
        ThrowStatementSyntax or ThrowExpressionSyntax => ErrorSiteKind.Throw,
        _ => null,
    };

    private static int LineOf(SyntaxNode node) => node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

    private static int StatementLineOf(SyntaxNode node) {
        var statement = node.AncestorsAndSelf().FirstOrDefault(n => n is StatementSyntax or SwitchExpressionArmSyntax);
        return statement is null ? LineOf(node) : LineOf(statement);
    }

    private static string MethodOf(SyntaxNode node) {
        foreach (var ancestor in node.Ancestors()) {
            switch (ancestor) {
                case MethodDeclarationSyntax m:
                    return m.Identifier.Text;
                case ConstructorDeclarationSyntax:
                    return ".ctor";
                case LocalFunctionStatementSyntax l:
                    return l.Identifier.Text;
                case PropertyDeclarationSyntax p:
                    return p.Identifier.Text;
            }
        }

        return "<type>";
    }

    // ── the message each site's source literal allows ────────────────────────────────

    private static (string? Pattern, string? Exempt) Message(SyntaxNode node, ErrorSiteKind kind) {
        switch (kind) {
            case ErrorSiteKind.Expect: {
                    var call = (InvocationExpressionSyntax)node;
                    var kindName = call.ArgumentList.Arguments[0].Expression is MemberAccessExpressionSyntax m ? m.Name.Identifier.Text : null;
                    return kindName is null ? (null, "expected kind is not a literal") : ($"Expected {Regex.Escape(kindName)}, got '.*' \\(.*\\)", null);
                }
            case ErrorSiteKind.ExpectIdentifier: {
                    var call = (InvocationExpressionSyntax)node;
                    return call.ArgumentList.Arguments[1].Expression is LiteralExpressionSyntax literal
                        ? ($"Expected {Regex.Escape(literal.Token.ValueText)}, got '.*'", null)
                        : (null, "label is not a literal");
                }
            default: {
                    var thrown = node switch {
                        ThrowStatementSyntax s => s.Expression,
                        ThrowExpressionSyntax e => e.Expression,
                        _ => null,
                    };
                    return ThrownMessage(thrown);
                }
        }
    }

    private static (string? Pattern, string? Exempt) ThrownMessage(ExpressionSyntax? thrown) {
        switch (thrown) {
            case InvocationExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.Text: "Error" } } call:
                return Pattern(call.ArgumentList.Arguments[0].Expression);
            case InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.Text: "GrammarError" }, Name.Identifier.Text: "Error" } } call:
                return Pattern(call.ArgumentList.Arguments[0].Expression);
            case ObjectCreationExpressionSyntax { Type: IdentifierNameSyntax { Identifier.Text: "FormatException" } } created:
                return Pattern(created.ArgumentList!.Arguments[0].Expression);
            case InvocationExpressionSyntax { Expression: IdentifierNameSyntax helper }:
                return (null, $"message built by helper {helper.Identifier.Text}");
            default:
                return (null, $"throw of {thrown?.Kind()}");
        }
    }

    private static (string? Pattern, string? Exempt) Pattern(ExpressionSyntax message) {
        var pattern = PatternOf(message);
        return pattern is null ? (null, "message is not a literal") : ("^" + pattern + "$", null);
    }

    private static string? PatternOf(ExpressionSyntax e) {
        switch (e) {
            case LiteralExpressionSyntax l when l.IsKind(SyntaxKind.StringLiteralExpression):
                return Regex.Escape(l.Token.ValueText);
            case ParenthesizedExpressionSyntax p:
                return PatternOf(p.Expression);
            case BinaryExpressionSyntax b when b.IsKind(SyntaxKind.AddExpression): {
                    var left = PatternOf(b.Left);
                    var right = PatternOf(b.Right);
                    return left is null || right is null ? null : left + right;
                }
            case ConditionalExpressionSyntax c: {
                    var whenTrue = PatternOf(c.WhenTrue);
                    var whenFalse = PatternOf(c.WhenFalse);
                    return whenTrue is null || whenFalse is null ? null : $"(?:{whenTrue}|{whenFalse})";
                }
            case InterpolatedStringExpressionSyntax i: {
                    var sb = new StringBuilder();
                    foreach (var content in i.Contents) {
                        if (content is InterpolatedStringTextSyntax text)
                            sb.Append(Regex.Escape(text.TextToken.ValueText.Replace("{{", "{", StringComparison.Ordinal).Replace("}}", "}", StringComparison.Ordinal)));
                        else
                            sb.Append(".*");
                    }

                    return sb.ToString();
                }
            default:
                return null;
        }
    }

    // ── lookup ────────────────────────────────────────────────────────────────────────

    /// <summary>The site at <paramref name="line"/> of a partial: an exact line, else the one statement that starts there.</summary>
    public static ErrorSite? At(string partial, int line) {
        var inPartial = Sites.Where(s => s.Partial == partial).ToList();
        var exact = inPartial.Where(s => s.Line == line).ToList();
        if (exact.Count == 1)
            return exact[0];
        if (exact.Count > 1)
            throw new InvalidOperationException($"Two scanned sites share {partial}:{line}: {string.Join(", ", exact.Select(s => s.Id))}");
        var statement = inPartial.Where(s => s.StatementLine == line).ToList();
        return statement.Count == 1 ? statement[0] : null;
    }

    // ── reach: the stack walk ─────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves <paramref name="text"/> through the same session door as <c>ResolverBaselineTests.Changes</c> and returns what it threw.
    /// A failure the table reported (through <see cref="DslCursor.MatchOrThrow"/>) carries the row elements it named, read from a
    /// <see cref="Poly.Grammar.MatchTrace"/> scope, in <see cref="Exception.Data"/>.
    /// </summary>
    public static Exception? Capture(string text) {
        using var trace = Poly.Grammar.MatchTrace.Begin();
        try {
            var session = DomainSession.ForSource(text, ExtensionCatalog.ProductAuthoring, ProductCatalog.Catalog);
            new DomainResolver(DslSyntaxTree.Parse(text), session).Resolve();
            return null;
        }
        catch (Exception ex) {
            if (trace.Observations is [.., { Failure: Poly.Grammar.MatchFailure<DslToken, DslTokenKind> failure }])
                ex.Data[TableLabelsKey] = LabelsOf(failure);
            return ex;
        }
    }

    private const string TableLabelsKey = "G-26b table labels";

    /// <summary>The row elements a reported failure names: its reported element first, then every expected element.</summary>
    private static List<string> LabelsOf(Poly.Grammar.MatchFailure<DslToken, DslTokenKind> failure) {
        var labels = new List<string> { TableId(failure.Rule, failure.Pattern, failure.Element) };
        foreach (var expected in failure.Expected) {
            var id = TableId(expected.Rule, expected.Pattern, expected.Element);
            if (!labels.Contains(id))
                labels.Add(id);
        }
        return labels;
    }

    /// <summary>A table row element as a site id: <c>table:rule/pattern#n</c>, n the element's 1-based position in its row.</summary>
    public static string TableId(string rule, string pattern, int element) => $"table:{rule}/{pattern}#{element + 1}";

    private static List<string>? _tableLabels;

    /// <summary>
    /// Every element that names itself in a failure report (an <c>Expect</c> label, a Word, a Run) or refuses (a <c>Refuse</c>),
    /// in the rules the resolver matches through <see cref="DslCursor.MatchOrThrow"/> (found by a scan of the partials for
    /// <c>MatchOrThrow("rule")</c>) and the rules their rows reference. Label reach requires each to be exercised.
    /// </summary>
    public static IReadOnlyList<string> TableLabels => _tableLabels ??= ScanTableLabels();

    private static List<string> ScanTableLabels() {
        var folder = Path.Combine(GuideBlocks.RepoRoot(), Directory);
        var rules = new List<string>();
        foreach (var path in System.IO.Directory.GetFiles(folder, "DomainResolver*.cs").OrderBy(p => p, StringComparer.Ordinal)) {
            foreach (Match m in Regex.Matches(File.ReadAllText(path), @"\bMatchOrThrow\(""([^""]+)""\)"))
                rules.Add(m.Groups[1].Value);
        }
        var grammar = DslGrammar.Core;
        for (var i = 0; i < rules.Count; i++) {
            foreach (var pattern in grammar.GetPatterns(rules[i])) {
                foreach (var element in pattern.Elements) {
                    var target = element switch {
                        Poly.Grammar.Ref<DslToken, DslTokenKind> r => r.RuleName,
                        Poly.Grammar.Repeat<DslToken, DslTokenKind> r => r.RuleName,
                        Poly.Grammar.LeftAssoc<DslToken, DslTokenKind> l => l.OperandRule,
                        Poly.Grammar.Optional<DslToken, DslTokenKind> { Inner: Poly.Grammar.Ref<DslToken, DslTokenKind> r } => r.RuleName,
                        _ => null,
                    };
                    if (target is not null && !rules.Contains(target))
                        rules.Add(target);
                }
            }
        }
        var labels = new List<string>();
        foreach (var rule in rules) {
            foreach (var pattern in grammar.GetPatterns(rule).OrderBy(p => p.Order)) {
                for (var e = 0; e < pattern.Elements.Count; e++) {
                    var element = pattern.Elements[e] is Poly.Grammar.Optional<DslToken, DslTokenKind> o ? o.Inner : pattern.Elements[e];
                    if (element is Poly.Grammar.IExpectedElement { Expect: not null } or Poly.Grammar.Word<DslToken, DslTokenKind>
                        or Poly.Grammar.Run<DslToken, DslTokenKind> or Poly.Grammar.Refuse<DslToken, DslTokenKind>)
                        labels.Add(TableId(rule, pattern.Name, e));
                }
            }
        }
        return labels;
    }

    /// <summary>
    /// The origin of <paramref name="ex"/>: a table row element when <see cref="DslCursor.MatchOrThrow"/> threw it, else the first
    /// frame that is not a <see cref="DslCursor"/> helper. A frame in a <c>DomainResolver</c> partial is a scanned site when its line
    /// maps to one; anything else (the BCL, the expression parser, the syntax tree) is classified <c>external:Type.Method</c>.
    /// </summary>
    public static ErrorOrigin OriginOf(Exception ex) {
        var frames = new StackTrace(ex, true).GetFrames();
        foreach (var frame in frames) {
            var method = frame.GetMethod();
            var type = method?.DeclaringType;
            if (type == typeof(DslCursor) && method?.Name == nameof(DslCursor.MatchOrThrow)) {
                // The table reported it: the origin is the row element, not the resolver line that asked.
                if (ex.Data[TableLabelsKey] is not List<string> { Count: > 0 } labels)
                    throw new InvalidOperationException($"MatchOrThrow threw with no traced failure: the table origin cannot be named. {ex.Message}");
                return new ErrorOrigin(labels[0], "table", 0) { Labels = labels };
            }
            if (type == typeof(DslCursor))
                continue;
            var file = frame.GetFileName();
            var line = frame.GetFileLineNumber();
            if (type?.Assembly != typeof(DomainResolver).Assembly)
                return new ErrorOrigin(null, $"external:{type?.Name}.{method?.Name}", 0);
            if (line <= 0)
                throw new InvalidOperationException($"No line number on {type?.Name}.{method?.Name}: the stack walk cannot prove reach. {ex.GetType().Name}: {ex.Message}");
            if (file is not null && Path.GetFileName(file).StartsWith("DomainResolver", StringComparison.Ordinal)) {
                var site = At(PartialOf(file), line);
                return site is null
                    ? new ErrorOrigin(null, $"resolver-non-site:{PartialOf(file)}.{method!.Name}", line)
                    : new ErrorOrigin(site.Id, "site", line);
            }

            return new ErrorOrigin(null, $"external:{type?.Name}.{method?.Name}", line);
        }

        throw new InvalidOperationException("The exception has no stack frames: the stack walk cannot prove reach.");
    }

    // ── fail-closed probe ─────────────────────────────────────────────────────────────

    /// <summary>
    /// The probe is sound only when line numbers exist and the JIT cannot have inlined the helpers away: a probe
    /// exception must show a line above zero, and the Poly and test assemblies must have their optimizer disabled.
    /// A failure here fails the test; it is never a skip.
    /// </summary>
    public static IEnumerable<string> ProbeProblems() {
        foreach (var assembly in new[] { typeof(DomainResolver).Assembly, typeof(ResolverErrorSiteScanner).Assembly }) {
            var debuggable = assembly.GetCustomAttribute<DebuggableAttribute>();
            if (debuggable is null || !debuggable.IsJITOptimizerDisabled)
                yield return $"{assembly.GetName().Name} is built with the JIT optimizer on; the stack walk can lose frames. Run the Debug configuration.";
        }

        var probeLine = ThrowProbe(out var thrown);
        var frame = new StackTrace(thrown, true).GetFrame(0);
        if (frame is null || frame.GetFileLineNumber() <= 0)
            yield return "A probe exception shows no line number: PDBs are not loaded.";
        else if (frame.GetFileLineNumber() != probeLine)
            yield return $"A probe exception shows line {frame.GetFileLineNumber()}, expected {probeLine}.";
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int ThrowProbe(out Exception thrown) {
        thrown = null!;
        try {
            return Throw();
        }
        catch (Exception ex) {
            thrown = ex;
            return ThrowLine;
        }
    }

    private static int ThrowLine;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Throw() {
        ThrowLine = CurrentLine() + 1;
        throw new InvalidOperationException("probe");
    }

    private static int CurrentLine([CallerLineNumber] int line = 0) => line;
}