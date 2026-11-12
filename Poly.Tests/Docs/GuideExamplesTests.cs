using System.Text.RegularExpressions;

namespace Poly.Tests.Docs;

/// <summary>
/// Every <c>```poly</c> fence in <c>Poly.Mcp/Docs/poly-dsl-guide.md</c> is verified through the same
/// compile path <c>poly_define</c> runs. The guide cannot drift from the code without a red test,
/// because each fence carries a marker line that says what it claims — a whole document
/// (<c>valid</c>), a fragment that only analyses in context (<c>fragment</c>), a document that fails
/// with a named code (<c>invalid</c>), or a block that cannot be a document (<c>skip</c>).
/// <para>
/// The marker is a comment line, so it renders as nothing: <c>&lt;!-- poly-verify: valid --&gt;</c>
/// directly above the opening fence, never separated by a blank line.
/// </para>
/// </summary>
public class GuideExamplesTests {
    private static readonly Regex LooksLikePoly = new(
        @"^\s*(?:[A-Z][A-Za-z0-9_]*:\s*(?:entity|stage|enum|value|policy|action|function|contract)\b"
        + @"|domain\s|uses\b|assign\s|create\s+in\b|transition\s+to\b|when\s|invoke\s|require\s|for\s+\w+\s+as\b)",
        RegexOptions.Compiled);

    /// <summary>
    /// Untagged fences that read as poly but are not tagged <c>```poly</c>. Keyed by the whitespace-normalised
    /// first non-blank line, not by line number, so a section move does not red; a new untagged example does.
    /// M-02 owns tagging them.
    /// </summary>
    private static readonly string[] UntaggedPolyShaped = [
        "assign Tier to match { Qty > 100 -> \"big\", Qty > 10 -> \"mid\", else -> \"small\" }",
    ];

    /// <summary>Counts per info string. Pinned by count, not by first line, so a reflow does not red.</summary>
    private static readonly (string Info, int Count)[] FenceCounts = [
        ("poly", 44),
        ("", 5),
        ("json", 1),
    ];

    /// <summary>Skip counts per reason. Pinned exactly: skips cannot grow silently (F2).</summary>
    private const int IllustrationSkips = 9;

    private const int DriftSkips = 4;

    /// <summary>Owners allowed on a skip: M-02 owns guide corrections (A1).</summary>
    private static readonly string[] SkipOwners = ["M-02"];

    private const string MarkerShape = "<!-- poly-verify: <kind> -->";


    [Test]
    public async Task EveryPolyFence_CarriesAWellFormedMarker() {
        var document = GuideBlocks.Load();
        var poly = document.Fences
            .Where(f => string.Equals(f.Info, "poly", StringComparison.Ordinal))
            .ToList();

        await Assert.That(poly.Count).IsEqualTo(GuideBlocks.PolyFences)
            .Because("the poly fence count is pinned; a moved or retyped fence must red here, not pass vacuously");

        var missing = poly
            .Where(fence => fence.Marker is null)
            .Select(fence => $"guide line {fence.Line}: no marker directly above the fence ({MarkerShape}); "
                             + $"first line: {FirstLine(fence.Code)}")
            .ToList();
        await Assert.That(missing.Count).IsEqualTo(0)
            .Because("every ```poly fence needs exactly one marker line above it:\n" + string.Join("\n", missing));

        var markers = poly.Count(fence => fence.Marker is not null);
        await Assert.That(markers).IsEqualTo(poly.Count)
            .Because("one marker per poly fence: a dangling marker already throws at load");
    }

    [Test]
    public async Task FenceInfos_ArePinned_ByCount() {
        var document = GuideBlocks.Load();
        var actual = document.Fences
            .GroupBy(fence => fence.Info, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var rendered = string.Join(", ", actual.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"'{pair.Key}'={pair.Value}"));
        var expected = string.Join(", ", FenceCounts.OrderBy(pair => pair.Info, StringComparer.Ordinal)
            .Select(pair => $"'{pair.Info}'={pair.Count}"));
        await Assert.That(rendered).IsEqualTo(expected)
            .Because("a block cannot dodge this harness by dropping its ```poly tag or switching fence character");
        await Assert.That(document.Fences.Any(fence => fence.Tilde)).IsFalse()
            .Because("the scan reads ``` fences only, so a ~~~ fence would be invisible");
    }

    [Test]
    public async Task UntaggedFences_ThatReadAsPoly_AreTheAllowlist() {
        var document = GuideBlocks.Load();
        var shaped = document.Fences
            .Where(fence => fence.Info.Length == 0)
            .SelectMany(fence => fence.Code
                .Where(line => LooksLikePoly.IsMatch(line))
                .Select(line => GuideBlocks.Normalise(line)))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        var allowlist = UntaggedPolyShaped.Order(StringComparer.Ordinal).ToList();

        await Assert.That(string.Join("\n", shaped)).IsEqualTo(string.Join("\n", allowlist))
            .Because("an untagged fence that reads as poly is an example the harness cannot see; "
                     + "either tag it ```poly or add it here with a follow-up");

        var untaggedCount = document.Fences.Count(fence => fence.Info.Length == 0);
        await Assert.That(untaggedCount).IsEqualTo(FenceCounts.Single(pair => pair.Info == "").Count)
            .Because("the untagged count is the surface this allowlist guards");
    }

    [Test]
    public async Task MarkerGrammar_OnHostileText_FailsClosed() {
        const string Good = "domain D\n\nA: entity {\n  N: Number\n}\n";
        var cases = new (string Name, string Guide, Action<string>? Wrappers)[]
        {
            ("unknown kind", $$"""
                {{Marker("nonsense")}}
                ```poly
                domain D
                ```
                """, null),
            ("dangling marker", $$"""
                {{Marker("valid")}}

                Not a fence.
                """, null),
            ("two markers above one fence", $$"""
                {{Marker("valid")}}
                {{Marker("valid")}}
                ```poly
                domain D
                ```
                """, null),
            ("invalid without a code", $$"""
                {{Marker("invalid:")}}
                ```poly
                domain D
                ```
                """, null),
            ("fragment without a wrapper", $$"""
                {{Marker("fragment:top")}}
                ```poly
                domain D
                ```
                """, null),
            ("wrapper with no placeholder", $$"""
                {{Marker("fragment:top wrapper:none-here")}}
                ```poly
                domain D
                ```
                """, root => Write(root, "none-here.poly.tmpl", "domain W\n")),
            ("wrapper with two placeholders", $$"""
                {{Marker("fragment:top wrapper:two-holes")}}
                ```poly
                domain D
                ```
                """, root => Write(root, "two-holes.poly.tmpl",
                "domain W\n// <<guide-block:top>>\n// <<guide-block:top>>\n")),
            ("wrapper without a domain header", $$"""
                {{Marker("fragment:top wrapper:no-domain")}}
                ```poly
                domain D
                ```
                """, root => Write(root, "no-domain.poly.tmpl", "// <<guide-block:top>>\n")),
            ("guide with zero poly fences", Good.Replace("```poly", "```text", StringComparison.Ordinal), null),
            ("tilde poly fence", $$"""
                {{Marker("valid")}}
                ~~~poly
                domain D
                ~~~
                """, null),
            ("skip with an unknown reason", $$"""
                {{Marker("skip:because owner:M-02")}}
                ```poly
                domain D
                ```
                """, null),
            ("wrapper on an illustration skip", $$"""
                {{Marker("skip:illustration wrapper:none-here owner:M-02")}}
                ```poly
                domain D
                ```
                """, root => Write(root, "none-here.poly.tmpl", "domain W\n// <<guide-block:top>>\n")),
            ("comment-only fragment", $$"""
                {{Marker("fragment:top wrapper:none-here")}}
                ```poly
                // just a note
                ```
                """, root => Write(root, "none-here.poly.tmpl", "domain W\n// <<guide-block:top>>\n")),
        };

        var failures = new List<string>();
        foreach (var (name, guide, wrappers) in cases) {
            var root = Path.Combine(Path.GetTempPath(), "q10-wrappers-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try {
                wrappers?.Invoke(root);
                _ = GuideBlocks.Load(guide, root);
                failures.Add($"{name}: expected InvalidOperationException, the guide loaded");
            }
            catch (InvalidOperationException expected) {
                if (expected.Message.Length == 0)
                    failures.Add($"{name}: threw without a message naming the problem");
            }
            finally {
                Directory.Delete(root, recursive: true);
            }
        }

        await Assert.That(failures.Count).IsEqualTo(0)
            .Because("every hostile guide shape must fail closed, naming the line or the wrapper:\n"
                     + string.Join("\n", failures));
        await Assert.That(cases.Length).IsEqualTo(13)
            .Because("R4 is pinned at eleven cases (A10), plus the comment-only fragment and the "
                     + "wrapper-on-illustration cases the fix rounds added");
    }

    [Test]
    public async Task EveryBlock_MatchesItsMarker() {
        var document = GuideBlocks.Load();
        var wrappers = document.Wrappers;
        var problems = new List<string>();

        foreach (var block in document.Blocks) {
            var label = $"guide line {block.Line} [{block.FirstLine}]";
            if (block.Marker is null) {
                problems.Add($"{label}: unmarked; raw probe: {GuideBlocks.Probe(block).Detail}");
                continue;
            }
            switch (block.Marker.Kind) {
                case GuideMarkerKind.Valid: {
                        var hasDomain = block.Code.Any(line =>
                            line.TrimStart().StartsWith("domain ", StringComparison.Ordinal));
                        if (!hasDomain) {
                            problems.Add($"{label}: 'valid' means a whole document, but the block has no domain header");
                            break;
                        }
                        var outcome = GuideBlocks.Document(block.Code);
                        if (outcome.Errors.Count > 0)
                            problems.Add($"{label}: 'valid' but {outcome.Detail}");
                        break;
                    }
                case GuideMarkerKind.Fragment: {
                        if (!wrappers.TryGetValue(block.Marker.Wrapper!, out var wrapper)) {
                            problems.Add($"{label}: wrapper '{block.Marker.Wrapper}' does not exist");
                            break;
                        }
                        if (!string.Equals(wrapper.Context, block.Marker.Context, StringComparison.Ordinal)) {
                            problems.Add($"{label}: marker says fragment:{block.Marker.Context} but "
                                         + $"wrapper '{wrapper.Id}' splices at {wrapper.Context}");
                            break;
                        }
                        var outcome = GuideBlocks.Splice(wrapper, block.Code);
                        if (outcome.Errors.Count > 0)
                            problems.Add($"{label}: fragment in '{wrapper.Id}' but {outcome.Detail}");
                        break;
                    }
                case GuideMarkerKind.Invalid: {
                        var wrapper = block.Marker.Wrapper is null
                            ? null
                            : wrappers.TryGetValue(block.Marker.Wrapper, out var found) ? found : null;
                        if (block.Marker.Wrapper is not null && wrapper is null) {
                            problems.Add($"{label}: wrapper '{block.Marker.Wrapper}' does not exist");
                            break;
                        }
                        var outcome = wrapper is null
                            ? GuideBlocks.Document(block.Code)
                            : GuideBlocks.Splice(wrapper, block.Code);
                        var errors = outcome.Errors;
                        if (errors.Count == 0) {
                            problems.Add($"{label}: 'invalid:{block.Marker.Code}' but the block analyses clean");
                            break;
                        }
                        var wrong = errors.Where(d => !string.Equals(d.Code, block.Marker.Code, StringComparison.Ordinal))
                            .ToList();
                        if (wrong.Count > 0) {
                            problems.Add($"{label}: 'invalid:{block.Marker.Code}' but "
                                         + string.Join("; ", wrong.Select(d => $"{d.Code}@{d.Line}")));
                            break;
                        }
                        var outside = errors.Where(d => !outcome.InBlock(d)).ToList();
                        if (outside.Count > 0) {
                            problems.Add($"{label}: diagnostic outside the block's spliced lines "
                                         + $"{outcome.FirstLine}-{outcome.LastLine}: "
                                         + string.Join("; ", outside.Select(d => $"{d.Code}@{d.Line}")));
                            break;
                        }
                        if (string.Equals(block.Marker.Code, "DSL0001", StringComparison.Ordinal)) {
                            var expectation = ParseExpectations
                                .FirstOrDefault(pair => pair.FirstLine == block.FirstLine);
                            if (expectation.FirstLine is null) {
                                problems.Add($"{label}: a DSL0001 block also pins a message substring; add one to ParseExpectations");
                            }
                            else if (!errors.Any(d => d.Message.Contains(expectation.Message, StringComparison.Ordinal))) {
                                problems.Add($"{label}: 'invalid:DSL0001' but no error carries '{expectation.Message}': "
                                             + outcome.Detail);
                            }
                        }
                        break;
                    }
                case GuideMarkerKind.Skip:
                    break;
                default:
                    problems.Add($"{label}: unhandled marker kind {block.Marker.Kind}");
                    break;
            }
        }

        await Assert.That(problems.Count).IsEqualTo(0)
            .Because("a marker that disagrees with the compiler is worse than no marker:\n"
                     + string.Join("\n", problems));
    }

    /// <summary>
    /// A <c>DSL0001</c> code alone is a weak pin (any parse error matches), so each parse-error block
    /// also pins a message substring, keyed by its first code line so a guide reflow does not red.
    /// </summary>
    private static readonly (string FirstLine, string Message)[] ParseExpectations = [];

    [Test]
    public async Task Skips_DoNotGrowSilently() {
        var document = GuideBlocks.Load();
        var skips = document.Blocks
            .Where(block => block.Marker?.Kind == GuideMarkerKind.Skip)
            .ToList();

        var illustration = skips.Count(b => b.Marker!.Reason == "illustration");
        var drift = skips.Count(b => b.Marker!.Reason == "drift");
        await Assert.That(illustration).IsEqualTo(IllustrationSkips)
            .Because("skips are pinned exactly: a wide skip set means the harness proves little");
        await Assert.That(drift).IsEqualTo(DriftSkips)
            .Because("a drift skip is a block that should validate; its count is pinned so drift cannot hide");

        var bad = skips
            .Where(block => !SkipOwners.Contains(block.Marker!.Owner!))
            .Select(block => $"guide line {block.Line}: skip owner '{block.Marker!.Owner}' is not one of "
                             + string.Join(", ", SkipOwners));
        await Assert.That(string.Join("\n", bad)).IsEqualTo(string.Empty)
            .Because("a skip needs an owner that can delete it");
    }

    /// <summary>
    /// A drift skip is a block that should validate and does not. The marker names the code it still
    /// fails with; this re-measures that code out of the block's own composition, so a block that starts
    /// failing differently, or starts passing, reds instead of hiding behind the skip.
    /// </summary>
    [Test]
    public async Task DriftSkips_StillFail() {
        var document = GuideBlocks.Load();
        var drift = document.Blocks
            .Where(block => block.Marker?.Kind == GuideMarkerKind.Skip
                            && string.Equals(block.Marker.Reason, "drift", StringComparison.Ordinal))
            .ToList();
        await Assert.That(drift.Count).IsEqualTo(DriftSkips)
            .Because("the drift count is pinned with the illustration count");

        var problems = new List<string>();
        foreach (var block in drift) {
            var code = block.Marker!.Code;
            if (code is null) {
                problems.Add($"guide line {block.Line}: a drift skip must name its code");
                continue;
            }
            var outcome = GuideBlocks.Compose(block, document.Wrappers);
            if (!outcome.Errors.Any(error =>
                    outcome.InBlock(error) && string.Equals(error.Code, code, StringComparison.Ordinal)))
                problems.Add($"guide line {block.Line} [{block.FirstLine}]: drift with {code}, but the block gives "
                             + $"{outcome.Codes} ({outcome.Detail})");
        }

        await Assert.That(problems.Count).IsEqualTo(0)
            .Because("a drift skip is re-measured, not asserted:\n" + string.Join("\n", problems));
    }

    [Test]
    public async Task Wrappers_AreHonest() {
        var wrappers = GuideBlocks.Wrappers();
        await Assert.That(wrappers.Count).IsEqualTo(GuideBlocks.WrapperCount)
            .Because("the wrapper count is pinned; every one must earn its place");

        var document = GuideBlocks.Load();
        var used = document.Blocks
            .Where(block => block.Marker?.Wrapper is not null)
            .Select(block => block.Marker!.Wrapper!)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var unused = wrappers.Keys.Where(id => !used.Contains(id)).Order(StringComparer.Ordinal).ToList();
        await Assert.That(string.Join(", ", unused)).IsEqualTo(string.Empty)
            .Because("an unused wrapper is context nobody needs");

        var missing = used.Where(id => !wrappers.ContainsKey(id)).ToList();
        await Assert.That(string.Join(", ", missing)).IsEqualTo(string.Empty)
            .Because("a marker may not name a wrapper that does not exist");

        var sensitive = new[] { "auth", "schema", "secret", "credential", "permission", "migration", "env" };
        var tagged = new List<string>();
        var blankNotClean = new List<string>();
        var controlNotBroken = new List<string>();
        var usesLines = new List<string>();
        var contextMismatch = new List<string>();

        foreach (var (id, wrapper) in wrappers) {
            var name = id + ".poly.tmpl";
            foreach (var word in sensitive) {
                if (id.Contains(word, StringComparison.Ordinal))
                    tagged.Add($"{name} contains '{word}'");
            }
            if (wrapper.Lines.Any(line => Regex.IsMatch(line, @"^\s*uses\b")))
                usesLines.Add(name);

            var blank = GuideBlocks.Splice(wrapper, []);
            if (blank.Errors.Count > 0)
                blankNotClean.Add($"{name}: {blank.Detail}");

            var control = GuideBlocks.Splice(wrapper, ["!!! this line is not poly !!!"]);
            if (control.Errors.Count == 0)
                controlNotBroken.Add($"{name}: the parse control analysed clean, so the wrapper proves nothing");

            // An unknown type resolves to DSL0001 before analysis in both contexts, so the semantic control
            // is a type mismatch: it must still be an analysis code, inside the block's lines.
            var semanticLines = string.Equals(wrapper.Context, "entity", StringComparison.Ordinal)
                ? new[] { "  Zz: Number", "  Zq: action { assign Zz to \"text\" }" }
                : ["Zz: entity {", "  N: Number", "  Zq: action { assign N to \"text\" }", "}"];
            var semantic = GuideBlocks.Splice(wrapper, semanticLines);
            var semanticErrors = semantic.Errors
                .Where(semantic.InBlock)
                .Where(error => error.Code is not ("DSL0001" or "DSL0002"))
                .ToList();
            if (semanticErrors.Count == 0)
                controlNotBroken.Add($"{name} ({wrapper.Context}): the semantic control gave no analysis error "
                                     + $"inside the block's lines {semantic.FirstLine}-{semantic.LastLine}: "
                                     + semantic.Codes);

            foreach (var block in document.Blocks.Where(b =>
                         b.Marker?.Kind == GuideMarkerKind.Fragment && b.Marker.Wrapper == id)) {
                if (!string.Equals(block.Marker!.Context, wrapper.Context, StringComparison.Ordinal))
                    contextMismatch.Add($"guide line {block.Line}: marker {block.Marker.Context}, wrapper {wrapper.Context}");
            }
        }

        await Assert.That(string.Join("\n", tagged)).IsEqualTo(string.Empty)
            .Because("a sensitive-looking wrapper path would trip the review classifier for no reason");
        await Assert.That(string.Join("\n", usesLines)).IsEqualTo(string.Empty)
            .Because("a wrapper is context, not a document: no wrapper declares 'uses'");
        await Assert.That(string.Join("\n", blankNotClean)).IsEqualTo(string.Empty)
            .Because("a wrapper with a blank placeholder must analyse clean, or its context is broken");
        await Assert.That(string.Join("\n", controlNotBroken)).IsEqualTo(string.Empty)
            .Because("a wrapper that cannot fail proves nothing about the blocks it hosts");
        await Assert.That(string.Join("\n", contextMismatch)).IsEqualTo(string.Empty)
            .Because("the marker kind must match the wrapper's placeholder context (F1)");
    }

    [Test]
    public async Task Wrappers_AreOutsideTheCorpusSweep() {
        var root = GuideBlocks.RepoRoot();
        var wrappersRoot = GuideBlocks.WrappersPath();
        var swept = Directory.EnumerateFiles(root, "*.poly", SearchOption.AllDirectories)
            .Where(path => !Path.GetRelativePath(root, path)
                .Split(Path.DirectorySeparatorChar)
                .Any(segment => segment is "bin" or "obj" or "node_modules" or ".git"))
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .ToList();

        var fromWrappers = swept
            .Where(path => path.StartsWith("Poly.Tests/Corpus/guide-wrappers/", StringComparison.Ordinal))
            .ToList();
        await Assert.That(string.Join("\n", fromWrappers)).IsEqualTo(string.Empty)
            .Because("StageEntryFactsTests globs every *.poly in the repo with skipped <= 20; a wrapper "
                     + "renamed to *.poly would consume that budget and change the sweep");

        var files = Directory.EnumerateFiles(wrappersRoot)
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .ToList();
        var documents = files
            .Where(name => name!.EndsWith(".poly", StringComparison.Ordinal))
            .ToList();
        await Assert.That(documents.Count).IsEqualTo(0)
            .Because("a wrapper named *.poly would enter the corpus sweep: " + string.Join(", ", documents));
        var templates = files
            .Where(name => name!.EndsWith(".poly.tmpl", StringComparison.Ordinal))
            .ToList();
        await Assert.That(templates.Count).IsEqualTo(GuideBlocks.WrapperCount)
            .Because("every wrapper is a template, and the count is pinned");
        var strangers = files
            .Where(name => !string.Equals(name, "README.md", StringComparison.Ordinal)
                           && !name!.EndsWith(".poly.tmpl", StringComparison.Ordinal))
            .ToList();
        await Assert.That(strangers.Count).IsEqualTo(0)
            .Because("the wrapper directory holds templates and its README only: " + string.Join(", ", strangers));
    }

    [Test]
    public async Task Markers_DoNotBlowTheShortGuide() {
        var document = GuideBlocks.Load();
        var fullMarkers = document.Fences.Count(fence => fence.Marker is not null);
        await Assert.That(fullMarkers).IsEqualTo(GuideBlocks.PolyFences)
            .Because("one marker line per poly fence, no blank lines");

        var response = McpTestTools.Dsl.GetDslGuide();
        await Assert.That(response.Success).IsTrue();
        var guide = response.Data!.GetType().GetProperty("guide")!.GetValue(response.Data) as string;
        await Assert.That(guide).IsNotNull();
        var lines = guide!.Split('\n').Length;
        var shortMarkers = Regex.Matches(guide, Regex.Escape(GuideBlocks.MarkerOpener)).Count;

        // The cap lives in McpSmokeTests. The re-measure procedure is in the wrappers README.
        await Assert.That(lines).IsLessThan(240)
            .Because("McpSmokeTests pins the short guide under 240 lines and the short guide includes "
                     + $"section 0; measured {lines} lines, {shortMarkers} markers in it");
        await Assert.That(shortMarkers).IsGreaterThan(0)
            .Because("the short guide must be built from the marked file, not a stale embedded copy");
    }

    [Test]
    public async Task HarnessVerdict_MatchesPolyDefine() {
        var document = GuideBlocks.Load();
        var wrappers = document.Wrappers;
        var (sessionId, _) = McpTestTools.Store.Create("Q10Parity");
        try {

            var samples = new List<(string Label, GuideBlock Block)>();
            var valid = document.Blocks.FirstOrDefault(b => b.Marker?.Kind == GuideMarkerKind.Valid);
            if (valid is not null)
                samples.Add(("valid", valid));
            var invalid = document.Blocks.FirstOrDefault(b =>
                b.Marker?.Kind == GuideMarkerKind.Invalid
                && !string.Equals(b.Marker.Code, "DSL0001", StringComparison.Ordinal));
            if (invalid is not null)
                samples.Add(("invalid", invalid));
            var parseError = document.Blocks.FirstOrDefault(b =>
                b.Marker?.Kind == GuideMarkerKind.Invalid
                && string.Equals(b.Marker.Code, "DSL0001", StringComparison.Ordinal));

            var mismatches = new List<string>();
            foreach (var (label, block) in samples) {
                var composed = GuideBlocks.Compose(block, wrappers);
                var mcp = McpTestTools.Dsl.ValidateDsl(sessionId, composed.Text);
                if (composed.Result.Succeeded != mcp.Success)
                    mismatches.Add($"{label} block at guide line {block.Line}: harness says {composed.Result.Succeeded}, "
                                   + $"validate_dsl says {mcp.Success}");
            }

            // A parse error is the case the two agree on least often by accident, so it is always sampled:
            // from the guide when one is marked, from a hostile document otherwise.
            var parseText = parseError is not null
                ? GuideBlocks.Compose(parseError, wrappers).Text
                : "domain Q10Parse\n\nA: entity {\n";
            var parseHarness = GuideBlocks.Analyze(parseText);
            var parseMcp = McpTestTools.Dsl.ValidateDsl(sessionId, parseText);
            if (parseHarness.Succeeded != parseMcp.Success)
                mismatches.Add($"parse-error document: harness says {parseHarness.Succeeded}, validate_dsl says {parseMcp.Success}");

            await Assert.That(string.Join("\n", mismatches)).IsEqualTo(string.Empty)
                .Because("the harness must be the same oracle as poly_define, or a green harness proves nothing");
            await Assert.That(samples.Count).IsGreaterThanOrEqualTo(1)
                .Because("at least the valid block is sampled");
        }
        finally {
            McpTestTools.Store.Remove(sessionId);
        }
    }

    private static string Marker(string body) => $"<!-- poly-verify: {body} -->";

    private static string FirstLine(IReadOnlyList<string> code) =>
        code.FirstOrDefault(line => line.Trim().Length > 0)?.Trim() ?? "(empty)";

    private static void Write(string root, string name, string content) =>
        File.WriteAllText(Path.Combine(root, name), content);
}