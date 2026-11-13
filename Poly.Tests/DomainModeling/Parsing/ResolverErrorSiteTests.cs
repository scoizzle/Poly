using System.Text.RegularExpressions;

using Poly.Grammar;

namespace Poly.Tests.DomainModeling.Parsing;

/// <summary>
/// G-26b's diagnostics oracle for the hand-written resolver (the 91 <c>Expect</c>, 55 <c>ExpectIdentifier</c> and 81 <c>throw</c> sites
/// that G-29 to G-33 delete; G-28 deleted the `public entity|value` head's) and, for a form the table reports, its row elements
/// (label reach: every labelled element and refusal in a rule the resolver matches through <c>MatchOrThrow</c> is named by a failure). Sites come from a Roslyn scan of <c>DomainResolver*.cs</c> and are named
/// <c>partial:method#ordinal</c>, never by line. REACH is proved by a stack walk on the thrown exception (the first frame above
/// <c>DslCursor</c>'s helpers is the call site) that fails closed. G-13a's S rows and sweep are the first source of reach;
/// <c>ResolverErrorSites.txt</c> holds NEW inputs only: <c>E</c> rows (key, declared site, the site a precedence input shadows,
/// input, full result: type, line, column, offset, length, message), <c>U</c> rows (a candidate an existing run already supplies, re-checked
/// live) and <c>X</c> rows (a required key with no input, and why). Known-wrong results carry an explicit tag (fixer and the wrong fragment).
/// Excluded: <c>DslExpressionParser</c>'s own sites (29, owned by G-34 and G-17) and diagnostics from later passes.
/// </summary>
public sealed class ResolverErrorSiteTests {
    private static readonly string[] DerivedPrefixes = ["site:", "label:", "off:", "kind:", "pair:", "ladder:", "defer:", "match:", "pub:"];

    private static readonly (string Partial, int Expect, int ExpectIdentifier, int Throw)[] PinnedTotals = [
        ("Actions", 7, 1, 0),
        ("Constraints", 11, 0, 6),
        ("Core", 2, 3, 5),
        ("Effects", 18, 12, 12),
        ("Entities", 21, 12, 26),
        ("Relationships", 0, 1, 8),
        ("Stages", 13, 10, 6),
        ("TopLevel", 19, 15, 16),
        ("Types", 0, 1, 2),
    ];

    private static readonly string[] ExemptFromLiteralCheck = [
        "Core:ResolveOnParseStack#3",
        "Core:ResolveOnParseStack#4",
        "Core:ResolveOnParseStack#5",
        // G-25: the message is DslLimits.StatementDepthDetail, a constant the scanner cannot read as a literal; its E row pins it.
        "Effects:EnterStatement#1",
        "Entities:ParseEntityAfterKind#6",
    ];

    private static ErrorFile File_ => ResolverErrorBaseline.ReadFile();

    // ── the scan ─────────────────────────────────────────────────────────────────────

    [Test]
    public async Task Scanner_PinsTheTotalsPerPartial_91_55_81() {
        var sites = ResolverErrorSiteScanner.Sites;
        foreach (var (partial, expect, identifier, @throw) in PinnedTotals) {
            var mine = sites.Where(s => s.Partial == partial).ToList();
            await Assert.That(mine.Count(s => s.Kind == ErrorSiteKind.Expect)).IsEqualTo(expect).Because(partial + " Expect");
            await Assert.That(mine.Count(s => s.Kind == ErrorSiteKind.ExpectIdentifier)).IsEqualTo(identifier).Because(partial + " ExpectIdentifier");
            await Assert.That(mine.Count(s => s.Kind == ErrorSiteKind.Throw)).IsEqualTo(@throw).Because(partial + " throw");
        }

        await Assert.That(sites.Count(s => s.Kind == ErrorSiteKind.Expect)).IsEqualTo(91);
        await Assert.That(sites.Count(s => s.Kind == ErrorSiteKind.ExpectIdentifier)).IsEqualTo(55);
        await Assert.That(sites.Count(s => s.Kind == ErrorSiteKind.Throw)).IsEqualTo(81);
        await Assert.That(sites.Select(s => s.Id).Distinct().Count()).IsEqualTo(sites.Count);
        await Assert.That(sites.Where(s => s.ExemptReason is not null).Select(s => s.Id)).IsEquivalentTo(ExemptFromLiteralCheck);
    }

    [Test]
    public async Task StackWalk_FailsClosedWithoutLineNumbersOrWithTheOptimizerOn() =>
        await Assert.That(ResolverErrorSiteScanner.ProbeProblems()).IsEmpty();

    // ── the file ─────────────────────────────────────────────────────────────────────

    [Test]
    public async Task File_HoldsEveryRequiredKeyAndNothingElse() {
        var file = File_;
        var reached = ResolverErrorBaseline.ExistingRuns.Where(r => r.Origin.IsSite).Select(r => r.Origin.SiteId!).Distinct().ToDictionary(s => s, _ => 1);
        var required = ResolverErrorBaseline.RequiredKeys(reached).ToList();
        var held = file.Rows.Select(r => r.Key).Concat(file.Subtracted.Select(r => r.Key)).Concat(file.Skipped.Select(r => r.Key)).ToList();

        await Assert.That(held.Distinct().Count()).IsEqualTo(held.Count).Because("a key appears once");
        await Assert.That(required.Distinct().Count()).IsEqualTo(required.Count);
        await Assert.That(required.Except(held)).IsEmpty().Because("a required key has no E, U or X row");
        await Assert.That(held.Except(required)).IsEmpty().Because("a row names a key nothing requires");
        await Assert.That(held.Where(k => !DerivedPrefixes.Any(p => k.StartsWith(p, StringComparison.Ordinal)))).IsEmpty();
    }

    [Test]
    public async Task DerivedRows_HoldTheInputsTheTableAndTheGrammarDerive() {
        var rows = File_.Rows.ToDictionary(r => r.Key, StringComparer.Ordinal);
        var failures = new List<string>();
        foreach (var (key, input) in ResolverErrorBaseline.GrammarPairs().Concat(ResolverErrorBaseline.KindInputs())) {
            if (rows.TryGetValue(key, out var row) && row.Input != input)
                failures.Add($"{key}: the file holds a different input than the table derives");
        }

        await Assert.That(failures).IsEmpty();
    }

    [Test]
    public async Task Rows_ReferToRealSitesAndEveryRowIsAnError() {
        var ids = ResolverErrorSiteScanner.Sites.Select(s => s.Id).Concat(ResolverErrorSiteScanner.TableLabels).ToHashSet(StringComparer.Ordinal);
        var failures = new List<string>();
        foreach (var row in File_.Rows) {
            if (!ids.Contains(row.Site))
                failures.Add($"{row.Key}: declared site {row.Site} is not a scanned site");
            if (row.Shadow.Length > 0 && !ids.Contains(row.Shadow))
                failures.Add($"{row.Key}: shadowed site {row.Shadow} is not a scanned site");
            if (row.Shadow == row.Site)
                failures.Add($"{row.Key}: a precedence row's winner and shadow are the same site");
            if (row.Result == "OK")
                failures.Add($"{row.Key}: the input does not fail");
        }
        foreach (var u in File_.Subtracted) {
            if (!ids.Contains(u.Site))
                failures.Add($"{u.Key}: subtracted site {u.Site} is not a scanned site");
        }

        await Assert.That(failures).IsEmpty();
    }

    [Test]
    public async Task Rows_MatchTheFrozenResultsAndDeclaredSites() =>
        await Assert.That(ResolverErrorBaseline.Diagnose(File_.Rows, ResolverErrorBaseline.Observe)).IsEmpty();

    [Test]
    public async Task Rows_ShowMessagesTheirSourceLiteralsAllow() {
        var failures = ResolverErrorBaseline.LiteralMismatches(File_.Rows, ResolverErrorBaseline.Observe);
        foreach (var run in ResolverErrorBaseline.ExistingRuns.Where(r => r.Origin.Class == "site")) {
            var site = ResolverErrorSiteScanner.Sites.Single(s => s.Id == run.Origin.SiteId);
            if (site.MessagePattern is not null && !Regex.IsMatch(ResolverErrorBaseline.DetailOf(run.Exception), site.MessagePattern, RegexOptions.Singleline))
                failures.Add($"{run.Key}: '{ResolverErrorBaseline.DetailOf(run.Exception)}' is not what {site.Id} allows");
        }

        await Assert.That(failures).IsEmpty();
    }

    [Test]
    public async Task PrecedenceRows_ShadowASiteSomeInputReaches() {
        var reached = ReachedSites();
        var failures = File_.Rows.Where(r => r.Shadow.Length > 0 && !reached.Contains(r.Shadow))
            .Select(r => $"{r.Key}: shadowed site {r.Shadow} is reached by no input").ToList();

        await Assert.That(failures).IsEmpty();
    }

    [Test]
    public async Task PrecedenceRows_ProveTheirShadowWithTheWinnerRepaired() {
        var file = File_;
        await Assert.That(ResolverErrorBaseline.ShadowProblems(file.Rows, file.Repairs, ResolverErrorBaseline.Observe)).IsEmpty();
    }

    [Test]
    public async Task SubtractedRows_AreStillSuppliedByTheExistingRows() {
        var supplied = ResolverErrorBaseline.ExistingRuns.Where(r => r.Origin.IsSite)
            .Select(r => (r.Origin.SiteId!, ResolverErrorBaseline.Sha(ResolverErrorBaseline.DetailOf(r.Exception)))).ToHashSet();
        var failures = File_.Subtracted.Where(u => !supplied.Contains((u.Site, u.DetailSha)))
            .Select(u => $"{u.Key}: no S row or sweep mutation produces {u.Site} with message {u.DetailSha} any more; add an E row").ToList();

        await Assert.That(failures).IsEmpty();
    }

    // ── reach ────────────────────────────────────────────────────────────────────────

    private static HashSet<string> ReachedSites() {
        var reached = ResolverErrorBaseline.ExistingRuns.Where(r => r.Origin.IsSite).Select(r => r.Origin.SiteId!).ToHashSet(StringComparer.Ordinal);
        foreach (var row in File_.Rows)
            reached.Add(row.Site);
        return reached;
    }

    [Test]
    public async Task EverySite_IsReachedOrUnreachableWithAReason() {
        var problems = ResolverErrorBaseline.ReachProblems(ReachedSites(), ResolverErrorSiteScanner.Sites.Select(s => s.Id), ResolverErrorBaseline.Unreachable);

        await Assert.That(problems).IsEmpty();
    }

    [Test]
    public async Task EveryTableLabel_IsNamedByAFailure() {
        var reached = ResolverErrorBaseline.LabelsReachedByExisting();
        foreach (var row in File_.Rows)
            reached.UnionWith(ResolverErrorBaseline.Observe(row.Input).Labels);

        await Assert.That(ResolverErrorSiteScanner.TableLabels).IsNotEmpty();
        await Assert.That(ResolverErrorBaseline.LabelReachProblems(reached, ResolverErrorSiteScanner.TableLabels)).IsEmpty();
    }

    [Test]
    public async Task ESiteRows_AddOnlySitesTheExistingRowsDoNotReach() {
        var existing = ResolverErrorBaseline.ReachBy("").Keys.ToHashSet();
        var redundant = File_.Rows.Where(r => r.Key.StartsWith("site:", StringComparison.Ordinal) && existing.Contains(r.Site))
            .Select(r => $"{r.Key}: an S row or a sweep mutation already reaches this site").ToList();

        await Assert.That(redundant).IsEmpty();
    }

    [Test]
    public async Task ReachCounts_AreReported() {
        static bool Scanned(string id) => !id.StartsWith("table:", StringComparison.Ordinal);
        var bySite = ResolverErrorBaseline.ReachBy("S:").Where(p => Scanned(p.Key)).ToDictionary();
        var byN = ResolverErrorBaseline.ReachBy("N:").Where(p => Scanned(p.Key)).ToDictionary();
        var existing = bySite.Keys.Union(byN.Keys).ToHashSet();
        var byE = File_.Rows.Select(r => r.Site).Where(Scanned).ToHashSet();
        var all = ResolverErrorSiteScanner.Sites;
        var unreachable = ResolverErrorBaseline.Unreachable.Length;

        // Pinned so a change in what the oracle reaches is a reviewed change, not a silent one. G-28 moved the `public entity|value`
        // head onto the marked-top row: its three sites went (reach moved to the row's 4 labels), TryTakeVisibility#1 became
        // VisibilityOf#1 and FoldMarkedTop#1 is new (both DefaultArm).
        await Assert.That(all.Count).IsEqualTo(227);
        await Assert.That(bySite.Count).IsEqualTo(72);
        await Assert.That(byN.Count).IsEqualTo(99);
        await Assert.That(existing.Count).IsEqualTo(124);
        await Assert.That(byE.Count(s => !existing.Contains(s))).IsEqualTo(33);
        await Assert.That(unreachable).IsEqualTo(70);
        await Assert.That(ResolverErrorSiteScanner.TableLabels.Count).IsEqualTo(4);
        await Assert.That(existing.Union(byE).Count() + unreachable).IsEqualTo(all.Count);
    }

    // ── known-wrong tags and the offset/length invariant ─────────────────────────────

    [Test]
    public async Task KnownWrongTags_AreCurrentAndEveryUnpositionedResultHasOne() {
        var results = ResolverErrorBaseline.FrozenG13aResults();
        foreach (var row in File_.Rows)
            results["E:" + row.Key] = row.Result;
        var problems = ResolverErrorBaseline.TagProblems(results, ResolverErrorBaseline.KnownWrong);

        await Assert.That(problems).IsEmpty();
    }

    /// <summary>
    /// G-25 (lock amendment 10): the statement depth error is built the way the expression parser builds its depth error,
    /// so it carries the head keyword's offset and length. It is the one resolver site that does; every other site
    /// keeps -1 and 0 until G-17.
    /// </summary>
    private static readonly string[] PositionedSites = ["Effects:EnterStatement#1"];

    [Test]
    public async Task EveryResolverGrammarException_HasOffsetMinusOneAndLengthZero() {
        var errors = ResolverErrorBaseline.ExistingRuns.Select(r => (r.Key, r.Exception)).ToList();
        var violations = ResolverErrorBaseline.OffsetLengthViolations(errors);
        foreach (var row in File_.Rows) {
            var seen = ResolverErrorBaseline.Observe(row.Input);
            if (PositionedSites.Contains(row.Site)) {
                if (seen.Offset < 0 || seen.Length <= 0)
                    violations.Add($"E:{row.Key}: a positioned site reported offset {seen.Offset}, length {seen.Length}");
            }
            else if (seen.Offset != -1 || seen.Length != 0)
                violations.Add($"E:{row.Key}: offset {seen.Offset}, length {seen.Length}");
        }

        await Assert.That(violations).IsEmpty();
        await Assert.That(File_.Rows.Count(r => PositionedSites.Contains(r.Site))).IsEqualTo(PositionedSites.Length);
    }

    // ── regeneration (re-record by fixer; never on changed consumers) ────────────────

    /// <summary>
    /// <c>POLY_REGEN_RESOLVER_ERRORS=&lt;fixer&gt;</c> re-records the result of every E row a <c>KnownWrong</c> tag of that fixer names;
    /// <c>all</c> re-records every E row (for G-17, after it flips the offset/length test). Inputs and declared sites never move.
    /// </summary>
    [Test]
    public async Task Regenerate_RerecordsResultsByFixer() {
        if (Environment.GetEnvironmentVariable("POLY_REGEN_RESOLVER_ERRORS") is not { Length: > 0 } selection)
            return;

        var file = File_;
        for (var i = 0; i < file.Rows.Count; i++) {
            if (file.Rows[i].Result.Length == 0)
                file.Rows[i] = file.Rows[i] with { Result = ResolverErrorBaseline.Observe(file.Rows[i].Input).Result };
        }
        for (var i = 0; i < file.Repairs.Count; i++) {
            if (file.Repairs[i].Result.Length == 0)
                file.Repairs[i] = file.Repairs[i] with { Result = ResolverErrorBaseline.Observe(file.Repairs[i].Input).Result };
        }
        var named = ResolverErrorBaseline.KnownWrong.Where(t => t.Fixer == selection).Select(t => t.Key).ToHashSet(StringComparer.Ordinal);
        for (var i = 0; i < file.Rows.Count; i++) {
            var row = file.Rows[i];
            if (selection == "new")
                continue;
            if (selection == "all" || named.Contains("E:" + row.Key))
                file.Rows[i] = row with { Result = ResolverErrorBaseline.Observe(row.Input).Result };
        }

        File.WriteAllText(ResolverErrorBaseline.FullPath, ResolverErrorBaseline.Write(file));
        await Assert.That(file.Rows.Count).IsGreaterThan(0);
    }

    // ── self-tests: each oracle fails when a COPY of its data is mutated ─────────────

    private static ErrorRow Row(string prefix) => File_.Rows.First(r => r.Key.StartsWith(prefix, StringComparison.Ordinal));

    [Test]
    public async Task SelfTest_AMessageWordChangedByOneWord_FailsTheRow() {
        var row = Row("kind:expect:");
        var mutated = row with { Result = row.Result.Replace("Expected", "Wanted", StringComparison.Ordinal) };
        var failures = ResolverErrorBaseline.Diagnose([mutated], ResolverErrorBaseline.Observe);

        await Assert.That(failures).IsNotEmpty();
        await Assert.That(failures[0]).Contains(row.Key);
    }

    [Test]
    public async Task SelfTest_AColumnMovedByOne_FailsTheRow() {
        var row = Row("kind:expect:");
        var match = Regex.Match(row.Result, @"@(\d+):(\d+)");
        var moved = row.Result.Replace(match.Value, $"@{match.Groups[1].Value}:{int.Parse(match.Groups[2].Value) + 1}", StringComparison.Ordinal);
        var failures = ResolverErrorBaseline.Diagnose([row with { Result = moved }], ResolverErrorBaseline.Observe);

        await Assert.That(failures).IsNotEmpty();
    }

    [Test]
    public async Task SelfTest_TheResultsOfTwoPrecedenceRowsSwapped_FailBoth() {
        var rows = File_.Rows.Where(r => r.Key.StartsWith("ladder:", StringComparison.Ordinal)).ToList();
        var first = rows[0];
        var second = rows.First(r => r.Result != first.Result);
        var swapped = new[] { first with { Result = second.Result }, second with { Result = first.Result } };
        var failures = ResolverErrorBaseline.Diagnose(swapped, ResolverErrorBaseline.Observe);

        await Assert.That(failures.Count(f => f.Contains("result", StringComparison.Ordinal))).IsEqualTo(2);
    }

    [Test]
    public async Task SelfTest_ARepairedInputThatFiresADifferentSite_IsAWrongRow() {
        var file = File_;
        var row = file.Rows.First(r => r.Shadow.Length > 0);
        var other = file.Rows.First(r => r.Site != row.Shadow && r.Shadow.Length == 0);
        var repairs = file.Repairs.Select(r => r.Key == row.Key ? r with { Input = other.Input, Result = other.Result } : r).ToList();
        var failures = ResolverErrorBaseline.ShadowProblems(file.Rows, repairs, ResolverErrorBaseline.Observe);

        await Assert.That(failures.Any(f => f.Contains("not the shadow", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task SelfTest_ADeclaredSiteThatDiffersFromTheObservedOne_FailsTheRow() {
        var row = Row("pub:1");
        var other = ResolverErrorSiteScanner.Sites.First(s => s.Id != row.Site).Id;
        var failures = ResolverErrorBaseline.Diagnose([row with { Site = other }], ResolverErrorBaseline.Observe);

        await Assert.That(failures).IsNotEmpty();
        await Assert.That(failures[0]).Contains("declared site");
    }

    [Test]
    public async Task SelfTest_ATagWhoseFragmentNoLongerAppears_IsStale() {
        var results = ResolverErrorBaseline.FrozenG13aResults();
        var (key, fixer, _) = ResolverErrorBaseline.KnownWrong[0];
        var problems = ResolverErrorBaseline.TagProblems(results, [(key, fixer, "a fragment the row never shows")]);

        await Assert.That(problems.Any(p => p.Contains("stale tag", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task SelfTest_AnUntaggedUnpositionedResult_IsReported() {
        var results = new Dictionary<string, string> {
            ["E:made-up"] = "FormatException @0:0 off=-1 len=0 :: no position",
            ["S:made/up"] = "ERR FormatException: no position",
        };
        var problems = ResolverErrorBaseline.TagProblems(results, []);

        await Assert.That(problems.Count).IsEqualTo(2);
    }

    [Test]
    public async Task SelfTest_AFixerOutsideTheClosedSetIsReported() {
        var results = ResolverErrorBaseline.FrozenG13aResults();
        var (key, _, fragment) = ResolverErrorBaseline.KnownWrong[0];
        var problems = ResolverErrorBaseline.TagProblems(results, [(key, "G-99", fragment)]);

        await Assert.That(problems.Any(p => p.Contains("closed set", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task SelfTest_AnOffsetOrLengthThatIsNotMinusOneAndZero_IsReported() {
        var violations = ResolverErrorBaseline.OffsetLengthViolations([
            ("with-offset", new GrammarException("x", 1, 1, offset: 4, length: 0)),
            ("with-length", new GrammarException("x", 1, 1, offset: -1, length: 2)),
            ("today", new GrammarException("x", 1, 1)),
        ]);

        await Assert.That(violations.Count).IsEqualTo(2);
    }

    [Test]
    public async Task SelfTest_ASourceLiteralThatDisagreesWithTheMessage_IsReported() {
        var row = Row("kind:expect:");
        var failures = ResolverErrorBaseline.LiteralMismatches([row],
            input => ResolverErrorBaseline.Observe(input) with { Detail = "Wanted something else, got 'x'" });

        await Assert.That(failures).IsNotEmpty();
    }

    [Test]
    public async Task SelfTest_AReachedSiteInTheUnreachableTable_AndAnUnreachedSiteWithoutAReason_BothFail() {
        var sites = ResolverErrorSiteScanner.Sites.Select(s => s.Id).ToList();
        var reachedAll = sites.ToHashSet(StringComparer.Ordinal);
        var staleEntry = ResolverErrorBaseline.ReachProblems(reachedAll, sites, [(sites[0], "LoopExit", "why")]);
        var missingReason = ResolverErrorBaseline.ReachProblems(new HashSet<string>(), sites, []);

        await Assert.That(staleEntry.Any(p => p.Contains("is reached", StringComparison.Ordinal))).IsTrue();
        await Assert.That(missingReason.Count).IsEqualTo(sites.Count);
    }

    [Test]
    public async Task SelfTest_ATableLabelNoFailureNames_IsReported() {
        var labels = ResolverErrorSiteScanner.TableLabels;
        var problems = ResolverErrorBaseline.LabelReachProblems(labels.Skip(1).ToHashSet(StringComparer.Ordinal), labels);

        await Assert.That(problems.Count).IsEqualTo(1);
        await Assert.That(problems[0]).StartsWith(labels[0] + ":");
    }

    [Test]
    public async Task SelfTest_ANewExpectInASource_ChangesTheScannedTotals() {
        var source = File.ReadAllText(Path.Combine(Poly.Tests.Docs.GuideBlocks.RepoRoot(), "Poly/DomainModeling/Language/DomainResolver.Types.cs"));
        var before = ResolverErrorSiteScanner.ScanSource("Types", source);
        var after = ResolverErrorSiteScanner.ScanSource("Types", source.Replace("private string ParseTypeName() {", "private string ParseTypeName() {\n        Expect(TokenKind.Colon);", StringComparison.Ordinal));

        await Assert.That(after.Count).IsEqualTo(before.Count + 1);
        await Assert.That(after.Count(s => s.Kind == ErrorSiteKind.Expect)).IsEqualTo(1);
    }
}