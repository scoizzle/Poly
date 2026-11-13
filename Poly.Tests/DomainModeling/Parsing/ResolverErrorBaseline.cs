using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

using Poly.DomainModeling.Language;
using Poly.Grammar;
using Poly.Tests.Docs;

namespace Poly.Tests.DomainModeling.Parsing;

/// <summary>One recorded error input: its key, the site it is declared to reach, the row it shadows (precedence rows), the text and the full result.</summary>
internal sealed record ErrorRow(string Key, string Site, string Shadow, string Input, string Result);

/// <summary>A candidate that an S row or a sweep mutation already supplies: the site and the SHA of the message it produced.</summary>
internal sealed record SubtractedRow(string Key, string Site, string DetailSha);

/// <summary>A required key that has no input, with the reason.</summary>
internal sealed record SkippedRow(string Key, string Reason);

/// <summary>A precedence row's companion: the same input with the winning fault repaired, so the shadowed site fires.</summary>
internal sealed record RepairRow(string Key, string Input, string Result);

internal sealed record ErrorFile(List<string> Header, List<ErrorRow> Rows, List<SubtractedRow> Subtracted, List<SkippedRow> Skipped) {
    public List<RepairRow> Repairs { get; } = [];
}

/// <summary>What the resolver did with one input: the result line, the site it was thrown from, and the structured error.</summary>
internal sealed record Observation(string Result, string? SiteId, string OriginClass, string? Detail, int Offset, int Length);

/// <summary>One S row or sweep mutation that threw: the source of the input and the site it reached.</summary>
internal sealed record ExistingRun(string Key, Exception Exception, ErrorOrigin Origin);

/// <summary>
/// G-26b's diagnostics baseline ("E"): the file format, the observation, the tables (unreachable sites, known-wrong
/// tags) and the keys every derived category must have. <c>ResolverErrorSiteTests</c> runs it; the self-tests mutate copies.
/// </summary>
internal static class ResolverErrorBaseline {
    public const string FilePath = "Poly.Tests/DomainModeling/Parsing/ResolverErrorSites.txt";

    private const string G13aPath = "Poly.Tests/DomainModeling/Parsing/ResolverBaseline.txt";

    // ── the closed owner set ─────────────────────────────────────────────────────────

    /// <summary>The rows that will replace a pinned-wrong result (owner ruling, #677 comment 6843661801).</summary>
    public static readonly string[] Fixers = ["G-13c", "T-15", "G-29", "G-05", "S-09", "G-27", "G-28", "G-32"];

    /// <summary>
    /// A result that is known wrong, by the row that will fix it. <c>Key</c> is <c>S:site/case</c> or <c>X:doc#token/filler/door</c>
    /// (G-13a rows, read from <c>ResolverBaseline.txt</c>) or <c>E:key</c>; <c>Fragment</c> is what the wrong result shows.
    /// A tag whose row no longer shows its fragment is stale and fails until it is deleted.
    /// </summary>
    public static readonly (string Key, string Fixer, string Fragment)[] KnownWrong = [
        // T-15: number and duration text surfaces as a raw .NET exception message.
        ("S:Constraints.length.min/overflow", "T-15", "OverflowException: Value was either too large"),
        ("S:Constraints.length.min/fraction", "T-15", "ERR FormatException: The input string '1.5' was not in a correct format."),
        ("S:Constraints.length.min/arabic-indic", "T-15", "ERR FormatException: The input string '٣' was not in a correct format."),
        ("S:Constraints.length.max/overflow", "T-15", "OverflowException: Value was either too large"),
        ("S:Entities.annotation.number/arabic-indic", "T-15", "ERR FormatException: The input string '٣' was not in a correct format."),
        ("S:Temporal.amount.message/fraction", "T-15", "InvalidOperationException: Duration amount '1.5' is not an integer."),
        ("S:Temporal.amount.message/overflow", "T-15", "InvalidOperationException: Duration amount '99999999999999999999' is not an integer."),
        // G-29: unsupported-keyword errors with no position, and the unknown `uses` id with no position.
        ("S:Effects.unsupported.check/ok", "G-29", "ERR FormatException: 'actor' is not supported in Phase 1a"),
        ("S:Effects.unsupported.check/case", "G-29", "ERR FormatException: 'Schedule' is not supported in Phase 1a"),
        ("S:Effects.unsupported.message/ok", "G-29", "ERR FormatException: 'PARALLEL' is not supported in Phase 1a"),
        ("S:Effects.unsupported.message/function", "G-29", "ERR FormatException: 'function' is not supported in Phase 1a"),
        ("S:Entities.typed-line.unsupported/ok", "G-29", "ERR FormatException: 'actor' is not supported in Phase 1a (used as type for 'Who')"),
        ("S:Entities.typed-line.unsupported/case", "G-29", "ERR FormatException: 'Schedule' is not supported in Phase 1a (used as type for 'Who')"),
        ("S:Entities.typed-line.unsupported/function", "G-29", "ERR FormatException: 'function' is not supported in Phase 1a (used as type for 'Who')"),
        ("S:Entities.fallback.unsupported/mis-keyword", "G-29", "ERR FormatException: 'for' is not supported in Phase 1a (used as type for 'Go')"),
        ("S:Relationships.navline.text/unsupported", "G-29", "ERR FormatException: 'actor' is not supported in Phase 1a (used as type for 'Owner')"),
        ("S:Relationships.navline.text/case", "G-29", "ERR FormatException: 'PARALLEL' is not supported in Phase 1a (used as type for 'Owner')"),
        ("X:entities#3/ident/R", "G-29", "InvalidOperationException: Unknown domain extension 'zz'"),
        ("X:expressions#3/ident/R", "G-29", "InvalidOperationException: Unknown domain extension 'zz'"),
        // G-05 keeps `actor` (S-09 owns the rest of TopLevelRejection's unpositioned refusals: `Function`, `schedule`); G-29 owns the inheritance error.
        ("S:TopLevel.rejection.unsupported/ok", "G-05", "ERR FormatException: 'actor' is not supported in Phase 1a (use 'entity' instead)"),
        ("S:TopLevel.rejection.unsupported/case", "S-09", "ERR FormatException: 'Function' is not supported in Phase 1a (use 'entity' instead)"),
        ("S:TopLevel.rejection.unsupported-message/ok", "S-09", "ERR FormatException: 'schedule' is not supported in Phase 1a (use 'entity' instead)"),
        ("S:TopLevel.rejection.inheritance/ok", "G-29", "ERR FormatException: Entity inheritance ('X: Base entity') is no longer supported."),
        ("S:TopLevel.rejection.inheritance-define/unicode", "G-29", "ERR FormatException: Entity inheritance ('X: Größe entity') is no longer supported."),
        // G-32 (#871): a `require` of a missing policy is reported at the end of the entity body, not at the `require` (frozen @33:1 for a require on line 23).
        ("E:site:Entities:ResolvePendingRequires#1", "G-32", "@33:1 off=-1 len=0 :: Action 'Act' requires policy 'Q'"),
        ("E:site:Entities:ResolvePendingRequires#2", "G-32", "@33:1 off=-1 len=0 :: Action 'Run' requires policy 'P'"),
        ("E:ladder:Entities:ResolvePendingRequires#1/Entities:ResolvePendingRequires#2", "G-32", "@5:2 off=-1 len=0 :: Action 'Go' requires policy 'M1'"),
        ("E:ladder:Entities:ResolvePendingRequires#2/Entities:ResolvePendingRequires#3", "G-32", "@5:2 off=-1 len=0 :: Action 'Go' requires policy 'M1'"),
        ("E:defer:order:requires-before-navs", "G-32", "@7:2 off=-1 len=0 :: Action 'Go' requires policy 'M'"),
        // G-28: the message does not show the token kind (a quoted string reads as `got 'value'`).
        ("E:pub:1", "G-28", "after 'public', got 'value'"),
        ("E:pub:2", "G-28", "after 'public', got 'str'"),
        ("E:pub:3", "G-28", "after 'public', got '5'"),
        ("E:pub:4", "G-28", "after 'public', got '{'"),
        ("E:pub:5", "G-28", "after 'public', got 'action'"),
        ("E:pub:8", "G-28", "after 'public', got ''"),
    ];

    /// <summary>Where a tag's fixing row lives when that is not obvious from the fixer: a site in G-30 or G-33 code is fixed by whichever row converts it.</summary>
    public static readonly Dictionary<string, string> KnownWrongNotes = new(StringComparer.Ordinal) {
        ["S:Effects.unsupported.check/ok"] = "site in DomainResolver.Effects.cs (G-33 converts the effect forms; G-29 owns the message)",
        ["S:Effects.unsupported.check/case"] = "site in DomainResolver.Effects.cs (G-33 converts the effect forms; G-29 owns the message)",
        ["S:Effects.unsupported.message/ok"] = "site in DomainResolver.Effects.cs (G-33 converts the effect forms; G-29 owns the message)",
        ["S:Effects.unsupported.message/function"] = "site in DomainResolver.Effects.cs (G-33 converts the effect forms; G-29 owns the message)",
        ["S:Entities.typed-line.unsupported/ok"] = "site in the entity body (G-30 converts it; G-29 owns the message)",
        ["S:Entities.typed-line.unsupported/case"] = "site in the entity body (G-30 converts it; G-29 owns the message)",
        ["S:Entities.typed-line.unsupported/function"] = "site in the entity body (G-30 converts it; G-29 owns the message)",
        ["S:Entities.fallback.unsupported/mis-keyword"] = "site in the entity body (G-30 converts it; G-29 owns the message)",
        ["S:Relationships.navline.text/unsupported"] = "site in the navigation line (G-30 converts it; G-29 owns the message)",
        ["S:Relationships.navline.text/case"] = "site in the navigation line (G-30 converts it; G-29 owns the message)",
        ["E:site:Entities:ResolvePendingRequires#1"] = "#871; the site is in the entity body (G-30 converts it), the position fix is G-32's action `require`",
        ["E:site:Entities:ResolvePendingRequires#2"] = "#871; the site is in the entity body (G-30 converts it), the position fix is G-32's action `require`",
        ["E:ladder:Entities:ResolvePendingRequires#1/Entities:ResolvePendingRequires#2"] = "#871",
        ["E:ladder:Entities:ResolvePendingRequires#2/Entities:ResolvePendingRequires#3"] = "#871",
        ["E:defer:order:requires-before-navs"] = "#871",
    };

    // ── sites no input can reach (stale-checked both ways) ───────────────────────────

    public static readonly string[] UnreachableCodes = ["LoopExit", "RowGuard", "CallerGuard", "DefaultArm", "Dead"];

    /// <summary>A scanned site that no input can reach, with a closed reason code and why. A reached site in this list fails.</summary>
    public static readonly (string Site, string Code, string Why)[] Unreachable = [
        ("Actions:ParseActionParameterList#1", "CallerGuard", "every caller tests for `(` first (`Current.Kind == LParen`, or the legacy-action row `Identifier LParen`)"),
        ("Actions:ParseActionParameterList#4", "LoopExit", "the `while (Current.Kind != RParen)` loop exits only on `)`"),
        ("Actions:ParseActionBody#2", "LoopExit", "the effect loop exits only on `}`"),
        ("Effects:ParseEffect#7", "DefaultArm", "every `effect` row has a case above; the default arm names a row no grammar defines"),
        ("Effects:ParseIfEffectCore#3", "LoopExit", "the then-body loop exits only on `}`"),
        ("Effects:ParseIfEffectCore#5", "LoopExit", "the else-body loop exits only on `}`"),
        ("Effects:ParseInvokeEffectTail#5", "LoopExit", "the binding loop exits only on `)`"),
        ("Effects:ParseGeneralForEffect#1", "CallerGuard", "ParseForEffect enters only when the token is an identifier followed by `in`"),
        ("Effects:ParseGeneralForEffect#2", "CallerGuard", "ParseForEffect enters only when the token after the name is `in`"),
        ("Effects:ParseGeneralForEffect#6", "LoopExit", "the for-body loop exits only on `}`"),
        ("Effects:ParseForEffect#3", "CallerGuard", "an identifier here is followed by `as` (any other next token returns or throws above), and a non-identifier fails the name check first"),
        ("Effects:ParseForEffect#16", "LoopExit", "the binding loop exits only on `)`"),
        ("Effects:ParsePropertyInitializers#2", "LoopExit", "the initializer loop exits only on `}`"),
        ("Entities:ParseEntity#1", "RowGuard", "top/entity matched `Identifier Colon Entity`"),
        ("Entities:ParseEntity#2", "RowGuard", "top/entity matched `Identifier Colon Entity`"),
        ("Entities:ParseEntity#3", "RowGuard", "top/entity matched `Identifier Colon Entity`"),
        ("Entities:ParseInitialStageClause#1", "CallerGuard", "IsInitialStageClause tests `initial (` first"),
        ("Entities:ParseKeyClause#1", "CallerGuard", "the header tests `key (` first"),
        ("Entities:ParseKeyClause#6", "LoopExit", "the member loop breaks only on `)`"),
        ("Entities:ParseEntityAfterKind#7", "RowGuard", "entity-body/stage matched `Identifier Colon Stage`"),
        ("Entities:ParseEntityAfterKind#8", "RowGuard", "entity-body/stage matched `Identifier Colon Stage`"),
        ("Entities:ParseEntityAfterKind#9", "RowGuard", "entity-body/marked-action matched `public action Identifier`"),
        ("Entities:ParseEntityAfterKind#10", "RowGuard", "entity-body/marked-action matched `public action Identifier`"),
        ("Entities:ParseEntityAfterKind#11", "RowGuard", "entity-body/marked-action matched `public action Identifier`"),
        ("Entities:ParseEntityAfterKind#12", "RowGuard", "entity-body/action matched `Identifier Colon Action`"),
        ("Entities:ParseEntityAfterKind#13", "RowGuard", "entity-body/action matched `Identifier Colon Action`"),
        ("Entities:ParseEntityAfterKind#14", "RowGuard", "entity-body/policy matched `Identifier Colon Policy`"),
        ("Entities:ParseEntityAfterKind#15", "RowGuard", "entity-body/policy matched `Identifier Colon Policy`"),
        ("Entities:ParseEntityAfterKind#16", "RowGuard", "entity-body/legacy-action matched `Identifier LParen`"),
        ("Entities:ParseEntityAfterKind#19", "RowGuard", "entity-body/typed-line matched `Identifier Colon Identifier`"),
        ("Entities:ParseEntityAfterKind#20", "RowGuard", "entity-body/typed-line matched `Identifier Colon Identifier`"),
        ("Entities:ParseEntityAfterKind#21", "RowGuard", "entity-body/typed-line matched `Identifier Colon Identifier`, so the type word is an identifier"),
        ("Entities:ParseEntityAfterKind#22", "Dead", "IsNavLine is false for an identifier only when it is an unsupported keyword, and CheckUnsupportedKeyword has already thrown for it"),
        ("Entities:ParseEntityAfterKind#23", "RowGuard", "entity-body/property matched `Identifier Colon <primitive>`"),
        ("Entities:ParseEntityAfterKind#24", "RowGuard", "entity-body/property matched `Identifier Colon <primitive>`"),
        ("Entities:ParseEntityAfterKind#25", "RowGuard", "entity-body/nav-many, nav-one and nav-owned matched `Identifier Colon <nav word>`"),
        ("Entities:ParseEntityAfterKind#26", "RowGuard", "entity-body/nav-many, nav-one and nav-owned matched `Identifier Colon <nav word>`"),
        ("Entities:ParseEntityAfterKind#27", "RowGuard", "entity-body/primitive-name matched `<primitive> Colon`"),
        ("Entities:ParseEntityAfterKind#29", "CallerGuard", "the fallback tests `Identifier` and `PeekIs(Colon)` first"),
        ("Entities:ParseEntityAfterKind#32", "LoopExit", "the member loop exits only on `}`"),
        ("Entities:ParseProperty#1", "DefaultArm", "ParseProperty is called only with a primitive type kind, and each has an arm"),
        ("Entities:ParseAnnotation#1", "CallerGuard", "TryParseRegisteredAnnotation calls it only after the annotation row matched or LooksLikeAnnotationCall saw `(`"),
        ("Entities:ParseAnnotation#5", "LoopExit", "the argument loop exits only on `)`"),
        ("Relationships:ParseNavLine#2", "CallerGuard", "the `Current.Kind != Identifier` test just above throws for any other kind"),
        ("Stages:ParseStage#6", "LoopExit", "the entry-body loop exits only on `}`"),
        ("Stages:ParseStage#9", "LoopExit", "the exit-body loop exits only on `}`"),
        ("Stages:ParseStage#10", "RowGuard", "stage-body/marked-stage-action matched `public action Identifier`"),
        ("Stages:ParseStage#11", "RowGuard", "stage-body/marked-stage-action matched `public action Identifier`"),
        ("Stages:ParseStage#12", "RowGuard", "stage-body/marked-stage-action matched `public action Identifier`"),
        ("Stages:ParseStage#14", "Dead", "stage-body/entry and stage-body/exit match every `entry` and `exit` token, so the fallback test never sees one"),
        ("Stages:ParseStage#17", "LoopExit", "the stage-body loop exits only on `}`"),
        ("Stages:ParseSubscription#6", "LoopExit", "the effect loop exits only on `}`"),
        ("Stages:ParseEntitySubscription#6", "LoopExit", "the effect loop exits only on `}`"),
        ("TopLevel:TryTakeVisibility#1", "DefaultArm", "the visibility rule has one pattern, `public`"),
        ("TopLevel:ParseEnumType#1", "RowGuard", "top/enum matched `Identifier Colon Enum`"),
        ("TopLevel:ParseEnumType#2", "RowGuard", "top/enum matched `Identifier Colon Enum`"),
        ("TopLevel:ParseEnumType#3", "RowGuard", "top/enum matched `Identifier Colon Enum`"),
        ("TopLevel:ParseValueType#1", "CallerGuard", "TryParseNamedTopLevel tests `Identifier Colon` and the word `value` first"),
        ("TopLevel:ParseValueType#2", "CallerGuard", "TryParseNamedTopLevel tests `Identifier Colon` and the word `value` first"),
        ("TopLevel:ParseFunction#7", "LoopExit", "the parameter loop exits only on `)`"),
        ("TopLevel:ParseContract#1", "CallerGuard", "TryParseNamedTopLevel tests `Identifier Colon` and the word `contract` first"),
        ("TopLevel:ParseContract#4", "CallerGuard", "TryParseNamedTopLevel tests `Identifier Colon` and the word `contract` first"),
        ("TopLevel:ParseContract#9", "CallerGuard", "the loop tests `Identifier Colon value` before the call"),
        ("TopLevel:ParseContract#10", "CallerGuard", "the loop tests `Identifier Colon value` before the call"),
        ("TopLevel:ParseBind#1", "CallerGuard", "TryParseNamedTopLevel tests `Identifier Colon` and the word `bind` first"),
        ("TopLevel:ParseBind#2", "CallerGuard", "TryParseNamedTopLevel tests `Identifier Colon` and the word `bind` first"),
        ("TopLevel:ExpectWord#1", "CallerGuard", "every ExpectWord call follows a test of the same word"),
        ("Types:ParseTypeName#1", "DefaultArm", "ParseTypeName enters the switch only for Text, NumberType or BooleanType, and each has an arm"),
        ("Types:ParseTypeName#2", "CallerGuard", "the `Current.Kind == Identifier` test just above"),
    ];

    public static readonly Dictionary<string, (string Code, string Why)> UnreachableBySite =
        Unreachable.ToDictionary(u => u.Site, u => (u.Code, u.Why), StringComparer.Ordinal);

    // ── file ─────────────────────────────────────────────────────────────────────────

    public static string Escape(string s) => DslTokenStreamBaselineTests.Escape(s);

    public static string Unescape(string s) => DslTokenStreamBaselineTests.Unescape(s);

    public static string Sha(string text, int length = 12) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..length];

    public static ErrorFile Read(string text) {
        var file = new ErrorFile([], [], [], []);
        foreach (var line in text.Split('\n')) {
            if (line.Length == 0)
                continue;
            if (line[0] == '#') {
                file.Header.Add(line);
                continue;
            }
            var f = line.Split('\t');
            switch (f[0]) {
                case "E" when f.Length == 6:
                    file.Rows.Add(new ErrorRow(f[1], f[2], f[3] == "-" ? "" : f[3], Unescape(f[4]), Unescape(f[5])));
                    break;
                case "U" when f.Length == 4:
                    file.Subtracted.Add(new SubtractedRow(f[1], f[2], f[3]));
                    break;
                case "R" when f.Length == 4:
                    file.Repairs.Add(new RepairRow(f[1], Unescape(f[2]), Unescape(f[3])));
                    break;
                case "X" when f.Length == 3:
                    file.Skipped.Add(new SkippedRow(f[1], Unescape(f[2])));
                    break;
                default:
                    throw new InvalidOperationException($"Unknown or malformed resolver error baseline record: {line}");
            }
        }

        return file;
    }

    public static string Write(ErrorFile file) {
        var sb = new StringBuilder();
        foreach (var h in file.Header)
            sb.Append(h).Append('\n');
        foreach (var r in file.Rows)
            sb.Append($"E\t{r.Key}\t{r.Site}\t{(r.Shadow.Length == 0 ? "-" : r.Shadow)}\t{Escape(r.Input)}\t{Escape(r.Result)}\n");
        foreach (var r in file.Repairs)
            sb.Append($"R\t{r.Key}\t{Escape(r.Input)}\t{Escape(r.Result)}\n");
        foreach (var u in file.Subtracted)
            sb.Append($"U\t{u.Key}\t{u.Site}\t{u.DetailSha}\n");
        foreach (var s in file.Skipped)
            sb.Append($"X\t{s.Key}\t{Escape(s.Reason)}\n");
        return sb.ToString();
    }

    public static string FullPath => Path.Combine(GuideBlocks.RepoRoot(), FilePath);

    public static ErrorFile ReadFile() => Read(File.ReadAllText(FullPath));

    // ── observation ──────────────────────────────────────────────────────────────────

    public static string DetailOf(Exception ex) => ex is GrammarException g ? g.Detail : ex.Message;

    /// <summary>The type, position, offset, length and message: everything the resolver says about an error.</summary>
    public static string ResultOf(Exception ex) => ex is GrammarException g
        ? $"GrammarException @{g.Line}:{g.Column} off={g.Offset} len={g.Length} :: {g.Detail}"
        : $"{ex.GetType().Name} @0:0 off=-1 len=0 :: {ex.Message}";

    public static Observation Observe(string input) {
        var ex = ResolverErrorSiteScanner.Capture(input);
        if (ex is null)
            return new Observation("OK", null, "none", null, -1, 0);
        var origin = ResolverErrorSiteScanner.OriginOf(ex);
        var (offset, length) = ex is GrammarException g ? (g.Offset, g.Length) : (-1, 0);
        return new Observation(ResultOf(ex), origin.SiteId, origin.Class, DetailOf(ex), offset, length);
    }

    // ── what G-13a already runs ──────────────────────────────────────────────────────

    private static readonly Lazy<IReadOnlyList<ExistingRun>> Existing = new(ComputeExisting, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Every S row (resolver door) and every sweep mutation (resolver door) that threw, with the site it reached.</summary>
    public static IReadOnlyList<ExistingRun> ExistingRuns => Existing.Value;

    public static (string Name, string Text)[] SweepFillers() {
        var field = typeof(ResolverBaselineTests).GetField("SweepFillers", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("ResolverBaselineTests.SweepFillers moved; the E oracle reads the sweep through it.");
        return ((string Name, string Text)[])field.GetValue(null)!;
    }

    private static IReadOnlyList<ExistingRun> ComputeExisting() {
        var runs = new List<ExistingRun>();
        foreach (var (site, @case, door, input) in ResolverBaselineTests.Cases()) {
            if (door != "R")
                continue;
            AddRun(runs, $"S:{site}/{@case}", input);
        }

        var fillers = SweepFillers();
        foreach (var (name, text) in ResolverBaselineTests.SweepDocuments) {
            var tokens = DslSyntaxTree.Parse(text).Tokens;
            for (var i = 0; i < tokens.Count - 1; i++) {
                foreach (var (filler, replacement) in fillers) {
                    var token = tokens[i];
                    var mutated = replacement.Length == 0
                        ? text[..token.Start]
                        : text[..token.Start] + replacement + text[(token.Start + token.Length)..];
                    AddRun(runs, $"N:{name}#{i}/{filler}", mutated);
                }
            }
        }

        return runs;
    }

    private static void AddRun(List<ExistingRun> runs, string key, string input) {
        Exception? ex;
        try {
            ex = ResolverErrorSiteScanner.Capture(input);
        }
        catch (Exception e) {
            ex = e;
        }

        if (ex is not null)
            runs.Add(new ExistingRun(key, ex, ResolverErrorSiteScanner.OriginOf(ex)));
    }

    /// <summary>The sites the S rows and the sweep reach, with the number of runs that reach each.</summary>
    public static Dictionary<string, int> ReachBy(string sourcePrefix) =>
        ExistingRuns.Where(r => r.Origin.IsSite && r.Key.StartsWith(sourcePrefix, StringComparison.Ordinal))
            .GroupBy(r => r.Origin.SiteId!).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

    // ── G-13a frozen results (for the tags) ──────────────────────────────────────────

    /// <summary>The recorded result of every S row and X row of <c>ResolverBaseline.txt</c>, keyed as <see cref="KnownWrong"/> keys.</summary>
    public static Dictionary<string, string> FrozenG13aResults() {
        var results = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in File.ReadAllLines(Path.Combine(GuideBlocks.RepoRoot(), G13aPath))) {
            var f = line.Split('\t');
            if (f[0] == "S" && f.Length == 6)
                results[$"S:{f[1]}/{f[2]}"] = Unescape(f[5]);
            else if (f[0] == "X" && f.Length == 6)
                results[$"X:{f[1]}#{f[2]}/{f[3]}/{f[4]}"] = Unescape(f[5]);
        }

        return results;
    }

    // ── pure compares (the self-tests mutate copies and expect these to fail) ────────

    /// <summary>Every row's result and declared site must equal what the resolver does now.</summary>
    public static List<string> Diagnose(IEnumerable<ErrorRow> rows, Func<string, Observation> observe) {
        var failures = new List<string>();
        foreach (var row in rows) {
            var seen = observe(row.Input);
            if (seen.Result != row.Result)
                failures.Add($"{row.Key}: result\n    expected {row.Result}\n    actual   {seen.Result}");
            if (seen.SiteId != row.Site)
                failures.Add($"{row.Key}: declared site {row.Site}, observed {seen.SiteId ?? seen.OriginClass}");
        }

        return failures;
    }

    /// <summary>
    /// Every precedence row (one with a Shadow) has a repaired companion whose recorded result is current and whose firing site IS the
    /// Shadow: the second fault really was in the winner's input. Pure.
    /// </summary>
    public static List<string> ShadowProblems(IEnumerable<ErrorRow> rows, IReadOnlyCollection<RepairRow> repairs, Func<string, Observation> observe) {
        var failures = new List<string>();
        var byKey = repairs.ToDictionary(r => r.Key, StringComparer.Ordinal);
        foreach (var row in rows.Where(r => r.Shadow.Length > 0)) {
            if (!byKey.TryGetValue(row.Key, out var repair)) {
                failures.Add($"{row.Key}: names a shadow and has no repaired input");
                continue;
            }
            var seen = observe(repair.Input);
            if (seen.Result != repair.Result)
                failures.Add($"{row.Key}: repaired result\n    expected {repair.Result}\n    actual   {seen.Result}");
            if (seen.SiteId != row.Shadow)
                failures.Add($"{row.Key}: with the winner repaired {seen.SiteId ?? seen.OriginClass} fires, not the shadow {row.Shadow}");
        }
        foreach (var key in byKey.Keys.Except(rows.Where(r => r.Shadow.Length > 0).Select(r => r.Key)))
            failures.Add($"{key}: a repaired input for a row with no shadow");
        return failures;
    }

    /// <summary>A row whose site has a message literal must show a message that literal allows.</summary>
    public static List<string> LiteralMismatches(IEnumerable<ErrorRow> rows, Func<string, Observation> observe) {
        var failures = new List<string>();
        foreach (var row in rows) {
            var site = ResolverErrorSiteScanner.Sites.SingleOrDefault(s => s.Id == row.Site);
            if (site?.MessagePattern is null)
                continue;
            var seen = observe(row.Input);
            if (seen.Detail is null || !Regex.IsMatch(seen.Detail, site.MessagePattern, RegexOptions.Singleline))
                failures.Add($"{row.Key}: '{seen.Detail}' is not what the source literal of {row.Site} allows ({site.MessagePattern})");
        }

        return failures;
    }

    /// <summary>
    /// A result that is unpositioned or not a grammar error is known wrong and must carry a tag; a tag must name a closed fixer, a
    /// result that exists and a fragment that result still shows.
    /// </summary>
    public static List<string> TagProblems(IReadOnlyDictionary<string, string> results, IEnumerable<(string Key, string Fixer, string Fragment)> tags) {
        var problems = new List<string>();
        var tagged = new HashSet<string>(StringComparer.Ordinal);
        foreach (var note in KnownWrongNotes.Keys.Where(k => !KnownWrong.Any(t => t.Key == k)))
            problems.Add($"{note}: a note for a key that has no tag");
        foreach (var (key, fixer, fragment) in tags) {
            if (!Fixers.Contains(fixer))
                problems.Add($"{key}: fixer '{fixer}' is not in the closed set");
            if (!results.TryGetValue(key, out var result))
                problems.Add($"{key}: tag names no row");
            else if (!result.Contains(fragment, StringComparison.Ordinal))
                problems.Add($"{key}: stale tag for {fixer}: the row no longer shows '{fragment}' (now: {result})");
            tagged.Add(key);
        }

        foreach (var (key, result) in results) {
            if (IsUnpositioned(key, result) && !tagged.Contains(key))
                problems.Add($"{key}: an unpositioned or non-grammar error with no tag: {result}");
        }

        return problems;
    }

    private static bool IsUnpositioned(string key, string result) => key.StartsWith("E:", StringComparison.Ordinal) || key.StartsWith("P:", StringComparison.Ordinal)
        ? result != "OK" && (!result.StartsWith("GrammarException @", StringComparison.Ordinal) || result.StartsWith("GrammarException @0:0 ", StringComparison.Ordinal))
        : result.StartsWith("ERR! ", StringComparison.Ordinal)
            || result.StartsWith("ERR FormatException:", StringComparison.Ordinal)
            || result.EndsWith(" @0:0", StringComparison.Ordinal);

    /// <summary>Every resolver GrammarException shows Offset -1 and Length 0 today (G-17 flips this one check).</summary>
    public static List<string> OffsetLengthViolations(IEnumerable<(string Key, Exception Exception)> errors) =>
        [.. errors.Where(e => e.Exception is GrammarException { Offset: not -1 } or GrammarException { Length: not 0 })
            .Select(e => $"{e.Key}: offset {((GrammarException)e.Exception).Offset}, length {((GrammarException)e.Exception).Length}")];

    /// <summary>Every scanned site is reached or listed with a reason; a listed site that is reached, unknown, or has an unknown reason code fails.</summary>
    public static List<string> ReachProblems(
        IReadOnlySet<string> reached,
        IEnumerable<string> siteIds,
        IEnumerable<(string Site, string Code, string Why)> unreachable) {
        var problems = new List<string>();
        var ids = siteIds.ToHashSet(StringComparer.Ordinal);
        var listed = unreachable.ToList();
        var listedSites = listed.Select(u => u.Site).ToHashSet(StringComparer.Ordinal);
        foreach (var id in ids.Where(id => !reached.Contains(id) && !listedSites.Contains(id)))
            problems.Add($"{id}: reached by no S row, sweep mutation or E row, and not listed as unreachable");
        foreach (var (site, code, why) in listed) {
            if (!ids.Contains(site))
                problems.Add($"{site}: listed as unreachable but is not a scanned site");
            else if (reached.Contains(site))
                problems.Add($"{site}: is reached; delete its unreachable entry ({why})");
            if (!UnreachableCodes.Contains(code))
                problems.Add($"{site}: reason code '{code}' is not in the closed set");
        }

        return problems;
    }

    // ── derived requirements ─────────────────────────────────────────────────────────

    public static bool IsUnreachable(ErrorSite site) => UnreachableBySite.ContainsKey(site.Id);

    /// <summary>The offending-token classes every label is tried with.</summary>
    public static readonly string[] Offenders = ["eof", "kw", "str"];

    /// <summary>The labels with at least one reachable site, each with its sites.</summary>
    public static Dictionary<string, List<ErrorSite>> ReachableLabels() =>
        ResolverErrorSiteScanner.Sites.Where(s => s.OffenderLabel is not null && !IsUnreachable(s))
            .GroupBy(s => s.OffenderLabel!).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

    public static IEnumerable<string> RequiredOffenderKeys() =>
        ReachableLabels().Keys.OrderBy(k => k, StringComparer.Ordinal).SelectMany(label => Offenders.Select(o => $"off:{label}:{o}"));

    /// <summary>The lexable token kinds: those whose sample text lexes back as that kind.</summary>
    public static List<DslTokenKind> LexableKinds() {
        var kinds = new List<DslTokenKind>();
        foreach (var kind in Enum.GetValues<DslTokenKind>()) {
            var sample = SampleOf(kind);
            if (sample is not null && new DslTokenReader(sample).Peek().Kind == kind)
                kinds.Add(kind);
        }

        return kinds;
    }

    public static string? SampleOf(DslTokenKind kind) {
        switch (kind) {
            case DslTokenKind.Identifier:
                return "zz";
            case DslTokenKind.Number:
                return "7";
            case DslTokenKind.StringLiteral:
                return "\"s\"";
            case DslTokenKind.EndOfFile:
                return "";
        }

        try {
            return DslGrammar.CanonicalText(kind);
        }
        catch (ArgumentOutOfRangeException) {
            return null;
        }
    }

    /// <summary>
    /// One representative site per message shape, as a template whose <c>¤</c> is where the offending token goes:
    /// <c>expect</c> (<c>Expect</c>, <c>got 'x' (Kind)</c>), <c>ident</c> (<c>ExpectIdentifier</c> with a label), <c>plain</c> (<c>got 'x'</c>).
    /// </summary>
    public static readonly (string Shape, string Template)[] KindShapes = [
        ("expect", "domain A\nX: entity {\n  N: Text length ¤"),
        ("ident", "domain A\nX: entity {\n  S: stage {\n    when ¤"),
        ("plain", "domain A\nfunction F(a: ¤"),
    ];

    public static string Fill(string template, string sample) =>
        sample.Length == 0 ? template[..template.IndexOf('¤')] : template.Replace("¤", sample, StringComparison.Ordinal);

    public static IEnumerable<(string Key, string Input)> KindInputs() {
        foreach (var (shape, template) in KindShapes) {
            foreach (var kind in LexableKinds())
                yield return ($"kind:{shape}:{kind}", Fill(template, SampleOf(kind)!));
        }
    }

    // grammar-row pairs: the rules the resolver dispatches over, each with the document context its rows appear in
    public static readonly (string Rule, string Context)[] DispatchRules = [
        ("top", "domain A\n¤"),
        ("entity-body", "domain A\nX: entity {\n  ¤"),
        ("stage-body", "domain A\nX: entity {\n  S: stage {\n    ¤"),
        ("effect", "domain A\nX: entity {\n  Go: action {\n    ¤"),
        ("annotation", "domain A\nX: entity {\n  N: Text ¤"),
        ("visibility", "domain A\n¤"),
    ];

    private static bool SameElement(IPatternElement<DslToken, DslTokenKind> a, IPatternElement<DslToken, DslTokenKind> b) => (a, b) switch {
        (MatchKind<DslToken, DslTokenKind> x, MatchKind<DslToken, DslTokenKind> y) => x.Kind == y.Kind,
        (Value<DslToken, DslTokenKind> x, Value<DslToken, DslTokenKind> y) => x.Kind == y.Kind,
        (MatchPredicate<DslToken, DslTokenKind> x, MatchPredicate<DslToken, DslTokenKind> y) => x.Label == y.Label,
        (Ref<DslToken, DslTokenKind> x, Ref<DslToken, DslTokenKind> y) => x.RuleName == y.RuleName,
        _ => false,
    };

    private static string? Sample(IPatternElement<DslToken, DslTokenKind> element) => element switch {
        MatchKind<DslToken, DslTokenKind> k => SampleOf(k.Kind),
        Value<DslToken, DslTokenKind> v => SampleOf(v.Kind),
        MatchPredicate<DslToken, DslTokenKind> p when p.Label == "primitive-type" => "Text",
        Ref<DslToken, DslTokenKind> r when r.RuleName == "visibility" => "public",
        _ => null,
    };

    /// <summary>
    /// One key and input per pair of rows of a dispatched rule that share a first element and then diverge, computed from the table:
    /// the shared prefix, then the first row's next element, then the end of the document.
    /// </summary>
    public static List<(string Key, string Input)> GrammarPairs() {
        var pairs = new List<(string, string)>();
        foreach (var (rule, context) in DispatchRules) {
            var patterns = DslGrammar.Core.GetPatterns(rule);
            for (var i = 0; i < patterns.Count; i++) {
                for (var j = i + 1; j < patterns.Count; j++) {
                    var a = patterns[i].Elements;
                    var b = patterns[j].Elements;
                    var shared = 0;
                    while (shared < a.Count && shared < b.Count && SameElement(a[shared], b[shared]))
                        shared++;
                    if (shared == 0)
                        continue;
                    var words = new List<string>();
                    for (var k = 0; k <= shared && k < a.Count; k++) {
                        if (Sample(a[k]) is not { Length: > 0 } word)
                            break;
                        words.Add(word);
                    }

                    pairs.Add(($"pair:{rule}:{patterns[i].Name}/{patterns[j].Name}", Fill(context, string.Join(' ', words))));
                }
            }
        }

        return pairs;
    }

    /// <summary>The throws that run after the whole document, or the whole entity, is parsed.</summary>
    public static List<ErrorSite> DeferredSites() =>
        [.. ResolverErrorSiteScanner.Sites.Where(s => s.Kind == ErrorSiteKind.Throw
            && (s.Method.StartsWith("ResolvePending", StringComparison.Ordinal)
                || (s.Partial == "Core" && s.ExemptReason is not null && s.ExemptReason.Contains("N2RelationshipNotSupported", StringComparison.Ordinal))))];

    /// <summary>Consecutive sites of one method, both reachable: the ladder pairs.</summary>
    public static List<(string Key, ErrorSite First, ErrorSite Second)> LadderPairs() {
        var pairs = new List<(string, ErrorSite, ErrorSite)>();
        foreach (var method in ResolverErrorSiteScanner.Sites.GroupBy(s => (s.Partial, s.Method))) {
            var sites = method.OrderBy(s => s.Ordinal).ToList();
            for (var i = 0; i + 1 < sites.Count; i++) {
                if (IsUnreachable(sites[i]) || IsUnreachable(sites[i + 1]))
                    continue;
                pairs.Add(($"ladder:{sites[i].Id}/{sites[i + 1].Id}", sites[i], sites[i + 1]));
            }
        }

        return pairs;
    }

    /// <summary>The dispatch sites, each tried matching 1, 2 and 3 tokens deep and then failing; a depth with no row that long is listed with its reason.</summary>
    public static readonly (string Rule, int Depth, string? NotApplicable)[] DispatchDepths = [
        ("top", 1, null), ("top", 2, null), ("top", 3, null),
        ("visibility", 1, null),
        ("visibility", 2, "the only visibility row, `public`, is one element long"),
        ("visibility", 3, "the only visibility row, `public`, is one element long"),
        ("entity-body", 1, null), ("entity-body", 2, null), ("entity-body", 3, null),
        ("annotation", 1, null), ("annotation", 2, null), ("annotation", 3, null),
        ("stage-body", 1, null), ("stage-body", 2, null), ("stage-body", 3, null),
        ("effect", 1, null), ("effect", 2, null), ("effect", 3, null),
    ];

    public static readonly string[] PublicPins = ["pub:1", "pub:2", "pub:3", "pub:4", "pub:5", "pub:6", "pub:7", "pub:8"];

    /// <summary>Every key the file must hold as an E row, a U row (an existing run supplies it) or an X row (with the reason it has no input).</summary>
    public static IEnumerable<string> RequiredKeys(IReadOnlyDictionary<string, int> reachedByExisting) {
        foreach (var site in ResolverErrorSiteScanner.Sites.Where(s => !IsUnreachable(s) && !reachedByExisting.ContainsKey(s.Id)))
            yield return $"site:{site.Id}";
        foreach (var key in RequiredOffenderKeys())
            yield return key;
        foreach (var (key, _) in KindInputs())
            yield return key;
        foreach (var (key, _) in GrammarPairs())
            yield return key;
        foreach (var site in DeferredSites())
            yield return $"defer:{site.Id}";
        yield return "defer:order:requires-before-navs";
        foreach (var (key, _, _) in LadderPairs())
            yield return key;
        foreach (var (rule, depth, na) in DispatchDepths.Where(d => d.NotApplicable is null))
            yield return $"match:{rule}:{depth}";
        foreach (var key in PublicPins)
            yield return key;
    }
}