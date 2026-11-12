using System.Globalization;
using System.Text.RegularExpressions;

using Poly.Compile;
using Poly.DomainModeling.Compile;
using Poly.Packs.Product;

namespace Poly.Tests.Docs;

/// <summary>What one <c>```poly</c> fence in the DSL guide claims about itself.</summary>
internal enum GuideMarkerKind {
    /// <summary>The fence text is a whole document and analyses with no error.</summary>
    Valid,

    /// <summary>The fence text is a fragment and analyses with no error once spliced into its wrapper.</summary>
    Fragment,

    /// <summary>The fence text is a document that fails with exactly the named diagnostic code.</summary>
    Invalid,

    /// <summary>The fence text cannot be a document, or should validate and does not.</summary>
    Skip,
}

/// <summary>
/// One parsed <c>&lt;!-- poly-verify: ... --&gt;</c> marker. The grammar is the guide's contract with
/// this harness: <c>valid</c>, <c>fragment:&lt;top|entity&gt; wrapper:&lt;id&gt;</c>,
/// <c>invalid:&lt;code&gt; [wrapper:&lt;id&gt;]</c>, <c>skip:illustration owner:&lt;slice&gt;</c>,
/// <c>skip:drift:&lt;code&gt; [wrapper:&lt;id&gt;] owner:&lt;slice&gt;</c>.
/// </summary>
internal sealed record GuideMarker(
    GuideMarkerKind Kind,
    string? Context,
    string? Wrapper,
    string? Code,
    string? Reason,
    string? Owner) {

    private static readonly Regex CodePattern = new(@"^(?:DSL|DM)[A-Z]{0,8}[0-9]{3,4}$", RegexOptions.Compiled);
    private static readonly Regex WrapperId = new(@"^[a-z][a-z0-9-]*$", RegexOptions.Compiled);
    private static readonly Regex OwnerPattern = new(@"^[A-Z]-[0-9]{2}$", RegexOptions.Compiled);

    public static GuideMarker Parse(string body, int line) {
        var tokens = body.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
            throw new InvalidOperationException($"Guide line {line}: empty poly-verify marker.");
        var head = tokens[0];
        if (string.Equals(head, "valid", StringComparison.Ordinal)) {
            if (tokens.Length != 1)
                throw new InvalidOperationException($"Guide line {line}: 'valid' takes no further tokens.");
            return new GuideMarker(GuideMarkerKind.Valid, null, null, null, null, null);
        }
        if (head.StartsWith("fragment:", StringComparison.Ordinal)) {
            var context = head["fragment:".Length..];
            if (context is not ("top" or "entity"))
                throw new InvalidOperationException(
                    $"Guide line {line}: fragment context '{context}' must be top or entity.");
            if (tokens.Length != 2 || !tokens[1].StartsWith("wrapper:", StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"Guide line {line}: a fragment needs exactly one wrapper:<id> token.");
            var wrapper = tokens[1]["wrapper:".Length..];
            if (!WrapperId.IsMatch(wrapper))
                throw new InvalidOperationException($"Guide line {line}: wrapper id '{wrapper}' is not lower-case kebab.");
            return new GuideMarker(GuideMarkerKind.Fragment, context, wrapper, null, null, null);
        }
        if (head.StartsWith("invalid:", StringComparison.Ordinal)) {
            var code = head["invalid:".Length..];
            if (!CodePattern.IsMatch(code))
                throw new InvalidOperationException(
                    $"Guide line {line}: invalid needs a diagnostic code (DSL0001, DMSTR006), got '{code}'.");
            string? wrapper = null;
            if (tokens.Length > 2)
                throw new InvalidOperationException(
                    $"Guide line {line}: invalid takes at most one wrapper:<id> token.");
            if (tokens.Length == 2) {
                if (!tokens[1].StartsWith("wrapper:", StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        $"Guide line {line}: '{tokens[1]}' is not a wrapper:<id> token.");
                wrapper = tokens[1]["wrapper:".Length..];
                if (!WrapperId.IsMatch(wrapper))
                    throw new InvalidOperationException($"Guide line {line}: wrapper id '{wrapper}' is not lower-case kebab.");
            }
            return new GuideMarker(GuideMarkerKind.Invalid, null, wrapper, code, null, null);
        }
        if (head.StartsWith("skip:", StringComparison.Ordinal)) {
            var reason = head["skip:".Length..];
            string? driftCode = null;
            if (reason.StartsWith("drift:", StringComparison.Ordinal)) {
                driftCode = reason["drift:".Length..];
                if (!CodePattern.IsMatch(driftCode))
                    throw new InvalidOperationException(
                        $"Guide line {line}: a drift skip names the diagnostic code it still fails with "
                        + $"(skip:drift:DSL0001), got '{driftCode}'.");
                reason = "drift";
            }
            if (driftCode is null && reason == "drift")
                throw new InvalidOperationException(
                    $"Guide line {line}: a drift skip names the code it still fails with (skip:drift:DSL0001).");
            if (driftCode is null && reason != "illustration")
                throw new InvalidOperationException(
                    $"Guide line {line}: skip reason '{reason}' must be illustration or drift:<code>.");
            string? skipWrapper = null;
            var ownerIndex = 1;
            if (tokens.Length == 3 && tokens[1].StartsWith("wrapper:", StringComparison.Ordinal)) {
                skipWrapper = tokens[1]["wrapper:".Length..];
                if (!WrapperId.IsMatch(skipWrapper))
                    throw new InvalidOperationException(
                        $"Guide line {line}: wrapper id '{skipWrapper}' is not lower-case kebab.");
                ownerIndex = 2;
            }
            if (skipWrapper is not null && driftCode is null)
                throw new InvalidOperationException(
                    $"Guide line {line}: wrapper:<id> is only for a drift skip; an illustration has no verdict.");
            if (tokens.Length != ownerIndex + 1
                || !tokens[ownerIndex].StartsWith("owner:", StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"Guide line {line}: a skip needs one owner:<slice> token, and a wrapper:<id> only on drift.");
            var owner = tokens[ownerIndex]["owner:".Length..];
            if (!OwnerPattern.IsMatch(owner))
                throw new InvalidOperationException($"Guide line {line}: owner '{owner}' is not a slice id (M-02).");
            return new GuideMarker(GuideMarkerKind.Skip, null, skipWrapper, driftCode, reason, owner);
        }
        throw new InvalidOperationException($"Guide line {line}: unknown poly-verify kind '{head}'.");
    }
}

/// <summary>One fenced code block in the guide: the fence line, its info string and its body.</summary>
internal sealed record GuideFence(int Line, string Info, IReadOnlyList<string> Code, GuideMarker? Marker, bool Tilde);

/// <summary>One <c>```poly</c> fence: the unit this harness verifies.</summary>
internal sealed record GuideBlock(int Line, IReadOnlyList<string> Code, GuideMarker? Marker) {
    /// <summary>The first non-blank code line, for failure messages that survive a reorder.</summary>
    public string FirstLine => Code.FirstOrDefault(l => l.Trim().Length > 0)?.Trim() ?? "(empty)";
}

/// <summary>
/// One compiled guide block: the composed document, plus the 1-based line range inside it that the
/// block's own text occupies. A diagnostic outside that range was caused by a wrapper, not the guide.
/// </summary>
internal sealed record GuideOutcome(string Text, int FirstLine, int LastLine, CompileResult Result) {
    public IReadOnlyList<CompileDiagnostic> Errors =>
        Result.Diagnostics.Where(d => d.IsError).ToList();

    /// <summary>Errors in report order, each as <c>CODE@line</c>, for one-line failure messages.</summary>
    public string Codes => Errors.Count == 0
        ? "0 errors"
        : string.Join("; ", Errors.Select(d => $"{d.Code}@{d.Line}"));

    public string Detail => Errors.Count == 0
        ? "0 errors"
        : string.Join("; ", Errors.Take(3).Select(d =>
            $"{d.Code}@{d.Line}: {(d.Message.Length > 140 ? d.Message[..140] : d.Message)}"));

    public bool InBlock(CompileDiagnostic diagnostic) =>
        diagnostic.Line >= FirstLine && diagnostic.Line <= LastLine;
}

/// <summary>A wrapper template: one <c>// &lt;&lt;guide-block:ctx&gt;&gt;</c> line, and a domain header.</summary>
internal sealed record GuideWrapper(string Id, string Context, IReadOnlyList<string> Lines, int PlaceholderIndex) {
    public IReadOnlyList<string> ContextLines => Lines;
}

/// <summary>The whole guide, scanned: every fence, in order, with markers attached.</summary>
internal sealed record GuideDocument(
    string Text,
    IReadOnlyList<GuideFence> Fences,
    IReadOnlyDictionary<string, GuideWrapper> Wrappers) {
    public IReadOnlyList<GuideBlock> Blocks => Fences
        .Where(f => string.Equals(f.Info, "poly", StringComparison.Ordinal))
        .Select(f => new GuideBlock(f.Line, f.Code, f.Marker))
        .ToList();
}

/// <summary>
/// Reads <c>Poly.Mcp/Docs/poly-dsl-guide.md</c>, attaches each fence's marker, and compiles a block
/// through the same <see cref="PolyCompiler"/> path <c>poly_define</c> uses: the product catalog,
/// the authoring seed, and the <c>Imported</c> default domain name. A loose path (raw parser,
/// <c>DomainSession.ForSource</c>) would validate examples that <c>poly_define</c> rejects.
/// </summary>
internal static class GuideBlocks {
    /// <summary>The token every marker line starts with. A marker is one line, directly above its fence.</summary>
    public const string MarkerOpener = "<!-- poly-verify:";

    /// <summary>How many <c>```poly</c> fences the guide carries. Pinned so a moved fence reds (R1).</summary>
    public const int PolyFences = 41;

    /// <summary>The wrapper count. Every one must be used by a block (A4).</summary>
    public const int WrapperCount = 13;

    private static readonly Regex MarkerLinePattern =
        new(@"^\s*<!--\s*poly-verify:(?<body>.*?)-->\s*$", RegexOptions.Compiled);

    private static readonly Regex FenceLinePattern =
        new(@"^(?<indent>[ \t]*)(?<fence>`{3,}|~{3,})(?<info>[^`~]*)$", RegexOptions.Compiled);

    private static readonly Regex PlaceholderPattern =
        new(@"^\s*//\s*<<guide-block:(?<context>top|entity)>>\s*$", RegexOptions.Compiled);

    private static readonly Regex DomainHeaderPattern =
        new(@"^\s*domain\s+[A-Za-z_][A-Za-z0-9_]*\s*$", RegexOptions.Compiled);

    private static readonly Regex UsesPattern = new(@"^\s*uses\b", RegexOptions.Compiled);

    public static string RepoRoot() {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null) {
            if (File.Exists(Path.Combine(dir.FullName, "AGENTS.md"))
                && Directory.Exists(Path.Combine(dir.FullName, "docs")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Repo root with AGENTS.md and docs/ not found.");
    }

    public static string GuidePath() => Path.Combine(RepoRoot(), "Poly.Mcp", "Docs", "poly-dsl-guide.md");

    public static string WrappersPath() => Path.Combine(RepoRoot(), "Poly.Tests", "Corpus", "guide-wrappers");

    public static string ReadGuide() => File.ReadAllText(GuidePath());

    public static GuideDocument Load() => Load(ReadGuide(), WrappersPath());

    /// <summary>
    /// Scans one guide text. Malformed markers, a marker that is not above a fence, a marker inside a
    /// fence, a marker above a non-poly fence, a tilde-poly fence and a guide with no poly fence all
    /// throw: a harness that silently skips them would pass over the thing it exists to check.
    /// </summary>
    public static GuideDocument Load(string guideText, string wrappersRoot) {
        ArgumentNullException.ThrowIfNull(guideText);
        var lines = guideText.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        var allMarkers = new Dictionary<int, string>();
        for (var index = 0; index < lines.Length; index++) {
            var match = MarkerLinePattern.Match(lines[index]);
            if (match.Success)
                allMarkers[index + 1] = match.Groups["body"].Value.Trim();
        }

        var fences = new List<GuideFence>();
        var attached = new HashSet<int>();
        var cursor = 0;
        while (cursor < lines.Length) {
            var open = FenceLinePattern.Match(lines[cursor]);
            if (!open.Success) {
                cursor++;
                continue;
            }
            var fence = open.Groups["fence"].Value;
            var info = open.Groups["info"].Value.Trim();
            var tilde = fence[0] == '~';
            var openLine = cursor + 1;
            cursor++;
            var code = new List<string>();
            var closed = false;
            while (cursor < lines.Length) {
                var close = FenceLinePattern.Match(lines[cursor]);
                if (close.Success
                    && close.Groups["fence"].Value[0] == fence[0]
                    && close.Groups["fence"].Value.Length >= fence.Length
                    && close.Groups["info"].Value.Trim().Length == 0) {
                    closed = true;
                    break;
                }
                if (MarkerLinePattern.IsMatch(lines[cursor]))
                    throw new InvalidOperationException(
                        $"Guide line {cursor + 1}: a poly-verify marker is inside a fence; it must sit on the line above.",
                        new InvalidOperationException($"fence opened at line {openLine}"));
                code.Add(lines[cursor]);
                cursor++;
            }
            if (!closed)
                throw new InvalidOperationException($"Guide line {openLine}: fence is never closed.");

            GuideMarker? marker = null;
            var aboveIndex = openLine - 2;
            if (aboveIndex >= 0 && allMarkers.TryGetValue(aboveIndex + 1, out var body)) {
                if (aboveIndex - 1 >= 0 && allMarkers.ContainsKey(aboveIndex))
                    throw new InvalidOperationException(
                        $"Guide line {aboveIndex}: two poly-verify markers above the fence at line {openLine}.");
                marker = GuideMarker.Parse(body, aboveIndex + 1);
                attached.Add(aboveIndex + 1);
            }
            if (marker is not null && !string.Equals(info, "poly", StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"Guide line {openLine}: a poly-verify marker sits above a '{info}' fence, not a poly fence.");
            if (tilde && string.Equals(info, "poly", StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"Guide line {openLine}: a poly fence must use ``` fences, not ~~~; the scan reads ``` only.");

            fences.Add(new GuideFence(openLine, info, code, marker, tilde));
            cursor++;
        }

        foreach (var line in allMarkers.Keys.Order()) {
            if (!attached.Contains(line))
                throw new InvalidOperationException(
                    $"Guide line {line}: dangling poly-verify marker; it is not on the line above a fence.");
        }

        var polyCount = fences.Count(f => string.Equals(f.Info, "poly", StringComparison.Ordinal));
        if (polyCount == 0)
            throw new InvalidOperationException(
                "The guide has no ```poly fences; the harness would pass vacuously. Check the guide path.");

        // Wrappers are validated at load, not when a block happens to use them: a broken template is a
        // broken harness even while its blocks are still unmarked.
        var wrappers = Wrappers(wrappersRoot);
        foreach (var fence in fences.Where(f => f.Marker?.Kind == GuideMarkerKind.Fragment)) {
            if (!fence.Code.Any(line => line.Trim().Length > 0
                                        && !line.TrimStart().StartsWith("//", StringComparison.Ordinal)))
                throw new InvalidOperationException(
                    $"Guide line {fence.Line}: a fragment needs at least one code line; "
                    + "this fence is blank or comment-only, so splice it as a skip.");
        }

        foreach (var fence in fences.Where(f => f.Marker?.Wrapper is not null)) {
            var marker = fence.Marker!;
            if (!wrappers.TryGetValue(marker.Wrapper!, out var wrapper))
                throw new InvalidOperationException(
                    $"Guide line {fence.Line}: wrapper '{marker.Wrapper}' does not exist in {wrappersRoot}.");
            if (marker.Kind == GuideMarkerKind.Fragment
                && !string.Equals(wrapper.Context, marker.Context, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"Guide line {fence.Line}: marker says fragment:{marker.Context} but wrapper "
                    + $"'{wrapper.Id}' splices at {wrapper.Context}.");
        }

        return new GuideDocument(guideText, fences, wrappers);
    }

    /// <summary>Every wrapper in the guide's wrapper directory, keyed by id.</summary>
    public static IReadOnlyDictionary<string, GuideWrapper> Wrappers() => Wrappers(WrappersPath());

    public static IReadOnlyDictionary<string, GuideWrapper> Wrappers(string root) {
        var wrappers = new SortedDictionary<string, GuideWrapper>(StringComparer.Ordinal);
        if (!Directory.Exists(root))
            return wrappers;
        foreach (var path in Directory.EnumerateFiles(root, "*.poly.tmpl").Order(StringComparer.Ordinal)) {
            var name = Path.GetFileName(path);
            var id = name[..^".poly.tmpl".Length];
            var lines = File.ReadAllLines(path);
            var index = -1;
            string? context = null;
            var placeholders = 0;
            for (var line = 0; line < lines.Length; line++) {
                var match = PlaceholderPattern.Match(lines[line]);
                if (!match.Success)
                    continue;
                placeholders++;
                index = line;
                context = match.Groups["context"].Value;
            }
            if (placeholders != 1)
                throw new InvalidOperationException(
                    $"Guide wrapper '{name}' must contain exactly one // <<guide-block:<ctx>> line; found {placeholders}.");
            if (lines.Any(line => UsesPattern.IsMatch(line)))
                throw new InvalidOperationException(
                    $"Guide wrapper '{name}' must not declare 'uses'; a wrapper is context, not a document.");
            if (!lines.Any(line => DomainHeaderPattern.IsMatch(line)))
                throw new InvalidOperationException($"Guide wrapper '{name}' must declare a domain header.");
            wrappers[id] = new GuideWrapper(id, context!, lines, index);
        }
        return wrappers;
    }

    /// <summary>Compiles text through the <c>poly_define</c> path. One instance per call: a compile evolves.</summary>
    public static CompileResult Analyze(string text) =>
        new PolyCompiler(ProductCatalog.Model, ProductCatalog.ImplicitIds,
                new CompileHooks { DefaultDomainName = "Imported" })
            .Analyze(text, "<workspace>");

    /// <summary>A whole document, compiled as-is.</summary>
    public static GuideOutcome Document(IReadOnlyList<string> lines) {
        var text = string.Join("\n", lines);
        return new GuideOutcome(text, 1, Math.Max(1, lines.Count), Analyze(text));
    }

    /// <summary>Splices block lines into a wrapper at its placeholder line. No reindent: the block keeps its own.</summary>
    public static GuideOutcome Splice(GuideWrapper wrapper, IReadOnlyList<string> blockLines) {
        ArgumentNullException.ThrowIfNull(wrapper);
        var composed = new List<string>(wrapper.Lines.Count + blockLines.Count);
        composed.AddRange(wrapper.Lines.Take(wrapper.PlaceholderIndex));
        var first = composed.Count + 1;
        composed.AddRange(blockLines);
        var last = composed.Count;
        composed.AddRange(wrapper.Lines.Skip(wrapper.PlaceholderIndex + 1));
        var text = string.Join("\n", composed);
        return new GuideOutcome(text, first, Math.Max(first, last), Analyze(text));
    }

    /// <summary>
    /// The block text as a document of its own, with a domain header added when it has none. This is
    /// the raw probe an unclassified block reports: its diagnostics name the companions it expects.
    /// </summary>
    public static GuideOutcome Probe(GuideBlock block) {
        var lines = block.Code;
        var first = lines.FirstOrDefault(l => l.Trim().Length > 0) ?? "";
        if (DomainHeaderPattern.IsMatch(first))
            return Document(lines);
        var text = "domain GuideProbe\n\n" + string.Join("\n", lines);
        return new GuideOutcome(text, 3, Math.Max(3, lines.Count + 2), Analyze(text));
    }

    /// <summary>The composed document for a block, resolved through its marker.</summary>
    public static GuideOutcome Compose(
        GuideBlock block, IReadOnlyDictionary<string, GuideWrapper> wrappers) {
        var marker = block.Marker
                     ?? throw new InvalidOperationException($"Guide line {block.Line}: no marker.");
        if (marker.Wrapper is null)
            return Probe(block);
        if (!wrappers.TryGetValue(marker.Wrapper, out var wrapper))
            throw new InvalidOperationException(
                $"Guide line {block.Line}: wrapper '{marker.Wrapper}' does not exist in {WrappersPath()}.");
        return Splice(wrapper, block.Code);
    }

    /// <summary>Whitespace-normalised text, so a claim substring survives a reflow (A11).</summary>
    public static string Normalise(string text) => Regex.Replace(text, @"\s+", " ").Trim();

    /// <summary>A bag value as text, for a claim probe's observed value.</summary>
    public static string Text(object? value) => value switch {
        null => "null",
        long number => number.ToString(CultureInfo.InvariantCulture),
        int number => number.ToString(CultureInfo.InvariantCulture),
        double number => number == Math.Floor(number)
            ? ((long)number).ToString(CultureInfo.InvariantCulture)
            : number.ToString("0.###", CultureInfo.InvariantCulture),
        decimal number => number == Math.Floor(number)
            ? ((long)number).ToString(CultureInfo.InvariantCulture)
            : number.ToString("0.###", CultureInfo.InvariantCulture),
        bool flag => flag ? "true" : "false",
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "null",
    };
}