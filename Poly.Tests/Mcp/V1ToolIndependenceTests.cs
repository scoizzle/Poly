using System.Text.RegularExpressions;

using Poly.Tests.Polyc;

namespace Poly.Tests.Mcp;

/// <summary>
/// The v1 tool classes (<c>DslTool</c>, <c>QueryTool</c>, ...) are on their way out. This ratchet lets
/// a test reference one only where the allow-list says so, with the exact reference count, so a call
/// that moves to <c>PolySurfaceHarness</c> must also lower its entry. It is deleted with the v1 tools (M-13).
/// <para>
/// A key is (file, v1 field or class): the seven <c>McpTestTools.X</c> fields and the class names.
/// A nested DTO such as <c>OracleTool.HtmlExportData</c> keys as <c>OracleTool</c>. Comment lines are
/// skipped the way <c>InertStatementTests</c> skips them.
/// </para>
/// </summary>
public partial class V1ToolIndependenceTests {
    internal sealed record Entry(string File, string Key, int Count, string Owner);

    private static readonly Entry[] Allowed = [
        // M-04
        new("Mcp/DomainFactsTests.cs", "McpTestTools.Query", 4, "M-04"),
        new("Mcp/McpSmokeTests.cs", "McpTestTools.Query", 28, "M-04+M-13"),
        new("Mcp/McpSqliteHttpHarnessTests.cs", "McpTestTools.Query", 1, "M-04"),
        // M-06
        new("Mcp/OracleToolTests.cs", "McpTestTools.Oracle", 8, "M-06"),
        // M-07 (no entries: Q-05 moved the runtime tests onto the harness)
        // M-09
        new("DomainModeling/Runtime/ModuleInstanceExecutorTests.cs", "RuntimeTool", 1, "M-09"),
        // M-10
        new("DomainModeling/Compile/ArtifactContributorCallerTests.cs", "McpTestTools.Oracle", 2, "M-10"),
        new("Mcp/HtmlToolTests.cs", "McpTestTools.Oracle", 3, "M-10"),
        new("Mcp/HtmlToolTests.cs", "OracleTool", 2, "M-10"),
        new("Mcp/JsonLdToolTests.cs", "McpTestTools.Oracle", 2, "M-10"),
        new("Mcp/JsonLdToolTests.cs", "OracleTool", 2, "M-10"),
        new("Mcp/JsonSchemaToolTests.cs", "McpTestTools.Dsl", 1, "M-10"),
        new("Mcp/JsonSchemaToolTests.cs", "McpTestTools.Oracle", 3, "M-10"),
        new("Mcp/JsonSchemaToolTests.cs", "McpTestTools.Query", 1, "M-10"),
        new("Mcp/JsonSchemaToolTests.cs", "OracleTool", 2, "M-10"),
        new("Mcp/McpSqliteHttpHarnessTests.cs", "McpTestTools.Oracle", 1, "M-10"),
        new("Mcp/ShaclToolTests.cs", "McpTestTools.Oracle", 2, "M-10"),
        new("Mcp/ShaclToolTests.cs", "OracleTool", 2, "M-10"),
        // M-11
        // M-13
        new("Docs/GuideExamplesTests.cs", "McpTestTools.Dsl", 1, "M-13"),
        new("DomainModeling/Analysis/DomainSemanticLookupFailClosedTests.cs", "McpTestTools.Evolve", 33, "M-13"),
        new("DomainModeling/Compile/ConstraintCoverageTests.cs", "McpTestTools.Dsl", 1, "M-13"),
        new("DomainModeling/Compile/ConstraintCoverageTests.cs", "McpTestTools.Evolve", 1, "M-13"),
        new("DomainModeling/Packs/ContractSurfaceFingerprintTests.cs", "EvolveTool", 1, "M-13"),
        new("Mcp/CacheSessionOwnershipTests.cs", "McpTestTools.Evolve", 2, "M-13"),
        new("Mcp/InspectPathTests.cs", "McpTestTools.Query", 2, "M-13"),
        new("Mcp/ManyToManyExportTests.cs", "McpTestTools.Evolve", 3, "M-13"),
        new("Mcp/McpContractAddTests.cs", "McpTestTools.Dsl", 1, "M-13"),
        new("Mcp/McpContractAddTests.cs", "McpTestTools.Evolve", 8, "M-13"),
        new("Mcp/McpContractAddTests.cs", "McpTestTools.Session", 1, "M-13"),
        new("Mcp/McpNameRuleTests.cs", "McpTestTools.Evolve", 13, "M-13"),
        new("Mcp/McpNameRuleTests.cs", "McpTestTools.Query", 1, "M-13"),
        new("Mcp/McpSmokeTests.cs", "McpTestTools.Evolve", 178, "M-13"),
        new("Mcp/OracleToolTests.cs", "McpTestTools.Dsl", 1, "M-13"),
        new("Mcp/OracleToolTests.cs", "McpTestTools.Evolve", 17, "M-13"),
        new("Mcp/OracleToolTests.cs", "McpTestTools.Query", 3, "M-13"),
        new("Mcp/OracleToolTests.cs", "McpTestTools.Session", 1, "M-13"),
        new("Mcp/SurfaceExtensionDogfoodTests.cs", "McpTestTools.Evolve", 3, "M-13"),
        new("Mcp/SurfaceParityTests.cs", "DslTool", 2, "M-13"),
        new("Mcp/SurfaceParityTests.cs", "OracleTool", 2, "M-13"),
        new("Mcp/UnifiedAddTests.cs", "McpTestTools.Evolve", 54, "M-13"),
        new("Mcp/UnifiedAddTests.cs", "McpTestTools.Policy", 1, "M-13"),
        new("Mcp/UnifiedAddTests.cs", "McpTestTools.Query", 9, "M-13"),
        new("Mcp/UnifiedAddTests.cs", "McpTestTools.Session", 1, "M-13"),
        new("Mcp/UnifiedRemoveTests.cs", "McpTestTools.Evolve", 28, "M-13"),
        new("Mcp/UnifiedRemoveTests.cs", "McpTestTools.Query", 4, "M-13"),
        new("Mcp/UniqueSurvivesRejectedEvolveTests.cs", "McpTestTools.Evolve", 4, "M-13"),
        new("Mcp/ValidateDslTests.cs", "McpTestTools.Query", 3, "M-13"),
        // Q-05
        new("Mcp/DomainReportToolTests.cs", "McpTestTools.Dsl", 1, "Q-05b"),
        new("Mcp/ExportRoundTripOrderTests.cs", "McpTestTools.Dsl", 4, "Q-05b"),
        new("Mcp/ExportRoundTripOrderTests.cs", "McpTestTools.Oracle", 1, "Q-05b"),
        new("Mcp/HtmlToolTests.cs", "McpTestTools.Dsl", 1, "Q-05b"),
        new("Mcp/HtmlToolTests.cs", "McpTestTools.Query", 1, "Q-05b"),
        new("Mcp/JsonLdToolTests.cs", "McpTestTools.Dsl", 1, "Q-05b"),
        new("Mcp/JsonLdToolTests.cs", "McpTestTools.Query", 1, "Q-05b"),
        new("Mcp/McpNameRuleTests.cs", "McpTestTools.Dsl", 4, "Q-05b"),
        new("Mcp/McpNameRuleTests.cs", "McpTestTools.Session", 4, "Q-05b"),
        new("Mcp/McpPackSharedInputsTests.cs", "McpTestTools.Dsl", 2, "Q-05b"),
        new("Mcp/ShaclToolTests.cs", "McpTestTools.Dsl", 1, "Q-05b"),
        new("Mcp/ShaclToolTests.cs", "McpTestTools.Query", 1, "Q-05b"),
        new("Mcp/DomainReportToolTests.cs", "McpTestTools.Query", 3, "Q-05b+M-10"),
        new("Mcp/PolyDefineEditTests.cs", "McpTestTools.Dsl", 4, "Q-05b+M-11"),
        new("Mcp/ValidateDslTests.cs", "McpTestTools.Dsl", 4, "Q-05b+M-11"),
        new("Mcp/ManyToManyExportTests.cs", "McpTestTools.Dsl", 2, "Q-05b+M-13"),
        new("DomainModeling/Compile/CompileRefusesErrorsTests.cs", "McpTestTools.Runtime", 1, "Q-05c"),
        new("DomainModeling/Compile/LibrarySelectionTests.cs", "McpTestTools.Dsl", 2, "Q-05c"),
        new("DomainModeling/Compile/LibrarySelectionTests.cs", "McpTestTools.Oracle", 1, "Q-05c"),
        new("DomainModeling/Compile/LibrarySelectionTests.cs", "McpTestTools.Runtime", 2, "Q-05c"),
        new("DomainModeling/DecimalMoneySpikeTests.cs", "McpTestTools.Dsl", 2, "Q-05c"),
        new("DomainModeling/DecimalMoneySpikeTests.cs", "McpTestTools.Runtime", 2, "Q-05c"),
        new("Interpretation/SkippedIfCreateInVmTests.cs", "McpTestTools.Dsl", 1, "Q-05c"),
        new("Interpretation/SkippedIfCreateInVmTests.cs", "McpTestTools.Runtime", 5, "Q-05c"),
        new("Mcp/DomainFactsTests.cs", "McpTestTools.Dsl", 3, "Q-05c"),
        new("Mcp/CreateInstanceCoercionTests.cs", "DomainToolResponse", 2, "Q-05d1"),
        new("Mcp/CreateInstanceCoercionTests.cs", "McpTestTools.Dsl", 4, "Q-05d1"),
        new("Mcp/CreateInstanceCoercionTests.cs", "McpTestTools.Policy", 3, "Q-05d1"),
        new("Mcp/CreateInstanceCoercionTests.cs", "McpTestTools.Runtime", 8, "Q-05d1"),
        new("Mcp/InstanceRenderingTests.cs", "DomainToolResponse", 1, "Q-05d1"),
        new("Mcp/InstanceRenderingTests.cs", "McpTestTools.Dsl", 2, "Q-05d1"),
        new("Mcp/InstanceRenderingTests.cs", "McpTestTools.Runtime", 6, "Q-05d1"),
        new("Mcp/RuntimeStageActionResolutionTests.cs", "DomainToolResponse", 2, "Q-05d1"),
        new("Mcp/RuntimeStageActionResolutionTests.cs", "McpTestTools.Dsl", 4, "Q-05d1"),
        new("Mcp/RuntimeStageActionResolutionTests.cs", "McpTestTools.Runtime", 9, "Q-05d1"),
        new("Mcp/SimulateCreateDogfoodTests.cs", "DomainToolResponse", 1, "Q-05d1"),
        new("Mcp/SimulateCreateDogfoodTests.cs", "McpTestTools.Dsl", 7, "Q-05d1"),
        new("Mcp/SimulateCreateDogfoodTests.cs", "McpTestTools.Policy", 3, "Q-05d1"),
        new("Mcp/SimulateCreateDogfoodTests.cs", "McpTestTools.Runtime", 9, "Q-05d1"),
        new("Mcp/SubscriptionSingleFireTests.cs", "McpTestTools.Dsl", 2, "Q-05d1"),
        new("Mcp/SubscriptionSingleFireTests.cs", "McpTestTools.Runtime", 2, "Q-05d1"),
        new("Mcp/CacheSessionOwnershipTests.cs", "McpTestTools.Dsl", 4, "Q-05d2"),
        new("Mcp/CacheSessionOwnershipTests.cs", "McpTestTools.Oracle", 1, "Q-05d2"),
        new("Mcp/CacheSessionOwnershipTests.cs", "McpTestTools.Runtime", 1, "Q-05d2"),
        new("Mcp/McpModuleRuntimeTests.cs", "McpTestTools.Dsl", 1, "Q-05d2"),
        new("Mcp/McpModuleRuntimeTests.cs", "McpTestTools.Runtime", 1, "Q-05d2"),
        new("Mcp/McpSqliteHttpHarnessTests.cs", "McpTestTools.Dsl", 3, "Q-05d2"),
        new("Mcp/McpSqliteHttpHarnessTests.cs", "McpTestTools.Runtime", 2, "Q-05d2"),
        new("Mcp/UniqueSurvivesExportTests.cs", "DomainToolResponse", 2, "Q-05d2"),
        new("Mcp/UniqueSurvivesExportTests.cs", "McpTestTools.Dsl", 6, "Q-05d2"),
        new("Mcp/UniqueSurvivesExportTests.cs", "McpTestTools.Oracle", 7, "Q-05d2"),
        new("Mcp/UniqueSurvivesExportTests.cs", "McpTestTools.Runtime", 9, "Q-05d2"),
        new("Mcp/UniqueSurvivesRejectedEvolveTests.cs", "McpTestTools.Dsl", 4, "Q-05d2"),
        new("Mcp/UniqueSurvivesRejectedEvolveTests.cs", "McpTestTools.Runtime", 8, "Q-05d2"),
        new("Mcp/SurfaceExtensionDogfoodTests.cs", "DomainToolResponse", 2, "Q-05e"),
        new("Mcp/SurfaceExtensionDogfoodTests.cs", "McpTestTools.Dsl", 27, "Q-05e"),
        new("Mcp/SurfaceExtensionDogfoodTests.cs", "McpTestTools.Policy", 19, "Q-05e"),
        new("Mcp/SurfaceExtensionDogfoodTests.cs", "McpTestTools.Runtime", 37, "Q-05e"),
        new("Mcp/SurfaceExtensionDogfoodTests.cs", "McpTestTools.Oracle", 5, "Q-05e+M-10"),
        new("Mcp/McpSmokeTests.cs", "DomainToolResponse", 1, "Q-05f1"),
        new("Mcp/McpSmokeTests.cs", "McpTestTools.Dsl", 107, "Q-05f1+M-13"),
        new("Mcp/McpSmokeTests.cs", "McpTestTools.Session", 2, "Q-05f1+M-13"),
        new("Mcp/McpSmokeTests.cs", "McpTestTools.Runtime", 143, "Q-05f2"),
        new("Mcp/McpSmokeTests.cs", "McpTestTools.Policy", 31, "Q-05f2+M-13"),
    ];

    [GeneratedRegex(@"\bMcpTestTools\.(?:Dsl|Query|Oracle|Runtime|Policy|Session|Evolve)\b|\b(?:DslTool|QueryTool|OracleTool|RuntimeTool|PolicyTool|SessionTool|EvolveTool|DomainTools|DomainToolResponse)\b")]
    private static partial Regex Reference();

    [Test]
    public async Task NoTestReferencesAV1ToolClass() {
        var scan = ScanTests(Path.Combine(PolycTestSupport.RepoRoot(), "Poly.Tests"));

        var problems = Compare(scan, Allowed);

        var seed = string.Join("\n", scan.OrderBy(p => p.Key.File, StringComparer.Ordinal).ThenBy(p => p.Key.Key, StringComparer.Ordinal)
            .Select(p => Render(new Entry(p.Key.File, p.Key.Key, p.Value, "?"))));
        await Assert.That(string.Join("\n", problems)).IsEqualTo(string.Empty)
            .Because("a v1 tool reference must be listed in V1ToolIndependenceTests.Allowed with its exact count and owner row; "
                + "migrate the call to PolySurfaceHarness or edit the line named above.\nLive scan, ready to paste:\n" + seed);
    }

    [Test]
    public async Task Compare_ExactTable_HasNoProblems() {
        var scan = Scan(("A.cs", "McpTestTools.Dsl", 3));

        var problems = Compare(scan, [new("A.cs", "McpTestTools.Dsl", 3, "M-13")]);

        await Assert.That(problems).IsEmpty();
    }

    [Test]
    public async Task Compare_UnlistedReference_FailsAsUnlisted() {
        var scan = Scan(("A.cs", "McpTestTools.Dsl", 3), ("B.cs", "QueryTool", 1));

        var problems = Compare(scan, [new("A.cs", "McpTestTools.Dsl", 3, "M-13")]);

        await Assert.That(problems.Count).IsEqualTo(1);
        await Assert.That(problems[0]).Contains("UNLISTED").And.Contains("B.cs").And.Contains("QueryTool");
    }

    [Test]
    public async Task Compare_CountChanged_FailsInBothDirections() {
        var scan = Scan(("A.cs", "McpTestTools.Dsl", 2), ("B.cs", "QueryTool", 5));

        var problems = Compare(scan, [new("A.cs", "McpTestTools.Dsl", 3, "M-13"), new("B.cs", "QueryTool", 4, "M-13")]);

        await Assert.That(problems.Count).IsEqualTo(2);
        await Assert.That(problems.All(p => p.StartsWith("COUNT", StringComparison.Ordinal))).IsTrue();
        await Assert.That(problems[0]).Contains("listed 3").And.Contains("found 2");
        await Assert.That(problems[1]).Contains("listed 4").And.Contains("found 5");
    }

    [Test]
    public async Task Compare_ListedPairWithNoReference_FailsAsStale() {
        var scan = Scan(("A.cs", "McpTestTools.Dsl", 3));

        var problems = Compare(scan, [new("A.cs", "McpTestTools.Dsl", 3, "M-13"), new("Gone.cs", "EvolveTool", 1, "M-13")]);

        await Assert.That(problems.Count).IsEqualTo(1);
        await Assert.That(problems[0]).Contains("STALE").And.Contains("Gone.cs").And.Contains("EvolveTool");
    }

    [Test]
    public async Task Compare_DuplicateEntry_Fails() {
        var scan = Scan(("A.cs", "McpTestTools.Dsl", 3));

        var problems = Compare(scan, [new("A.cs", "McpTestTools.Dsl", 3, "M-13"), new("A.cs", "McpTestTools.Dsl", 3, "M-04")]);

        await Assert.That(problems.Any(p => p.StartsWith("DUPLICATE", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task CountReferences_KeysFieldsAndClasses_SkipsCommentsAndStore() {
        string[] lines = [
            "var a = McpTestTools.Dsl.ApplyDsl(s, d); var b = McpTestTools.Dsl.ExportDsl(s);",
            "var c = McpTestTools.Oracle.ExportDomainToHtml(s); OracleTool.HtmlExportData d = null;",
            "var s = McpTestTools.Store.Create(\"x\");",
            "    // McpTestTools.Evolve.Add(...)",
            "    * RuntimeTool in a doc comment",
            "    /* DslTool */",
            "var t = typeof(PolySessionTool); var u = new DomainToolResponse(true, \"m\");",
            "reflect(typeof(RuntimeTool));",
        ];

        var counts = CountReferences(lines);

        await Assert.That(counts["McpTestTools.Dsl"]).IsEqualTo(2);
        await Assert.That(counts["McpTestTools.Oracle"]).IsEqualTo(1);
        await Assert.That(counts["OracleTool"]).IsEqualTo(1);
        await Assert.That(counts["DomainToolResponse"]).IsEqualTo(1);
        await Assert.That(counts["RuntimeTool"]).IsEqualTo(1);
        await Assert.That(counts.Keys.OrderBy(k => k, StringComparer.Ordinal))
            .IsEquivalentTo(["DomainToolResponse", "McpTestTools.Dsl", "McpTestTools.Oracle", "OracleTool", "RuntimeTool"]);
    }

    private static Dictionary<(string File, string Key), int> Scan(params (string File, string Key, int Count)[] rows) =>
        rows.ToDictionary(r => (r.File, r.Key), r => r.Count);

    private static Dictionary<string, int> CountReferences(IEnumerable<string> lines) {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var line in lines) {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("//", StringComparison.Ordinal)
                || trimmed.StartsWith("*", StringComparison.Ordinal)
                || trimmed.StartsWith("/*", StringComparison.Ordinal))
                continue;
            foreach (Match match in Reference().Matches(line))
                counts[match.Value] = counts.GetValueOrDefault(match.Value) + 1;
        }
        return counts;
    }

    private static Dictionary<(string File, string Key), int> ScanTests(string testsRoot) {
        var scan = new Dictionary<(string File, string Key), int>();
        foreach (var path in Directory.EnumerateFiles(testsRoot, "*.cs", SearchOption.AllDirectories)) {
            var relative = Path.GetRelativePath(testsRoot, path).Replace('\\', '/');
            var rooted = "/" + relative;
            if (rooted.Contains("/bin/", StringComparison.Ordinal) || rooted.Contains("/obj/", StringComparison.Ordinal))
                continue;
            if (relative is "Mcp/V1ToolIndependenceTests.cs" or "TestHelpers/McpTestTools.cs")
                continue;
            foreach (var (key, count) in CountReferences(File.ReadLines(path)))
                scan[(relative, key)] = count;
        }
        return scan;
    }

    private static List<string> Compare(IReadOnlyDictionary<(string File, string Key), int> scan, IEnumerable<Entry> allowed) {
        var problems = new List<string>();
        var listed = new Dictionary<(string File, string Key), Entry>();
        foreach (var entry in allowed) {
            if (!listed.TryAdd((entry.File, entry.Key), entry))
                problems.Add($"DUPLICATE {entry.File} {entry.Key}: listed more than once; delete one of the lines.");
        }

        foreach (var (pair, found) in scan.OrderBy(p => p.Key.File, StringComparer.Ordinal).ThenBy(p => p.Key.Key, StringComparer.Ordinal)) {
            if (!listed.TryGetValue(pair, out var entry))
                problems.Add($"UNLISTED {pair.File} {pair.Key}: {found} reference(s); add the line {Render(new Entry(pair.File, pair.Key, found, "<owner row>"))} to Allowed, or migrate the call.");
            else if (entry.Count != found)
                problems.Add($"COUNT {pair.File} {pair.Key}: listed {entry.Count}, found {found}; change the count on that Allowed line to {found}.");
        }
        foreach (var entry in listed.Values.OrderBy(e => e.File, StringComparer.Ordinal).ThenBy(e => e.Key, StringComparer.Ordinal)) {
            if (!scan.ContainsKey((entry.File, entry.Key)))
                problems.Add($"STALE {entry.File} {entry.Key}: listed {entry.Count} for {entry.Owner} but the file no longer references it; delete that Allowed line.");
        }
        return problems;
    }

    private static string Render(Entry entry) => $"new(\"{entry.File}\", \"{entry.Key}\", {entry.Count}, \"{entry.Owner}\"),";
}