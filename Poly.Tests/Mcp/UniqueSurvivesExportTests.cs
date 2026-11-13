namespace Poly.Tests.Mcp;

/// <summary>
/// #387: a <c>unique</c> property must stay enforced no matter which read-only tool ran last.
/// <para>
/// The defect being pinned: <c>export_domain_to_csharp</c> emits through the session the domain's
/// <c>uses</c> ids opened, while the executor runs a module lowered by a session that also carries
/// the persistence overlay (which owns <c>UniqueConstraintCompiler</c>). Both sessions bind the same
/// <see cref="Poly.DomainModeling.Analysis.RuntimeAnalysisCache"/> entry, and binding a different
/// session evicted the module the executor was running, so the next create re-lowered without the
/// unique check.
/// </para>
/// <para>
/// These tests go through the real tool surface rather than the cache, because the guarantee the
/// caller depends on is the tool's, not the cache's.
/// </para>
/// </summary>
public class UniqueSurvivesExportTests {
    private const string Dsl = """
        domain ExportUnique
        Agent: entity {
          Handle: Text unique
          Relabel: action (handle: Text) {
            assign Handle to handle
          }
        }
        """;

    [Test]
    public async Task Create_DuplicateUnique_AfterCSharpExport_StillFails() {
        var (sessionId, _) = McpTestTools.Store.Create("ExportUnique");
        var applied = McpTestTools.Surface.Define(sessionId, Dsl);
        await Assert.That(applied.Success).IsTrue();

        // A live executor exists before the export, which is the order that broke.
        var first = McpTestTools.Surface.Create(sessionId, "Agent", """{"Handle":"sam"}""");
        await Assert.That(first.Ok).IsTrue();

        var exported = McpTestTools.Surface.Export(sessionId, "csharp");
        await Assert.That(exported.Success).IsTrue();

        var duplicate = McpTestTools.Surface.Create(sessionId, "Agent", """{"Handle":"sam"}""");
        await Assert.That(duplicate.Ok).IsFalse()
            .Because("exporting the C# must not disable a unique constraint the domain declared");
        await Assert.That(duplicate.Summary).Contains("Unique constraint");
        await Assert.That(duplicate.Summary).Contains("Handle");
    }

    [Test]
    public async Task Create_DuplicateUnique_WhenExportRanFirst_StillFails() {
        var (sessionId, _) = McpTestTools.Store.Create("ExportUnique");
        var applied = McpTestTools.Surface.Define(sessionId, Dsl);
        await Assert.That(applied.Success).IsTrue();

        var exported = McpTestTools.Surface.Export(sessionId, "csharp");
        await Assert.That(exported.Success).IsTrue();

        var first = McpTestTools.Surface.Create(sessionId, "Agent", """{"Handle":"sam"}""");
        await Assert.That(first.Ok).IsTrue();

        var duplicate = McpTestTools.Surface.Create(sessionId, "Agent", """{"Handle":"sam"}""");
        await Assert.That(duplicate.Ok).IsFalse()
            .Because("an export before the executor exists must not leave it lowering without the unique check");
        await Assert.That(duplicate.Summary).Contains("Unique constraint");
    }

    /// <summary>
    /// Assign-time unique is the second path that reads the same module, so it is the second place
    /// an evicted module shows up. Kept separate so a fix that only repairs create is not enough.
    /// </summary>
    [Test]
    public async Task Assign_DuplicateUnique_AfterCSharpExport_StillFails() {
        var (sessionId, _) = McpTestTools.Store.Create("ExportUnique");
        var applied = McpTestTools.Surface.Define(sessionId, Dsl);
        await Assert.That(applied.Success).IsTrue();

        var sam = McpTestTools.Surface.Create(sessionId, "Agent", """{"Handle":"sam"}""");
        await Assert.That(sam.Ok).IsTrue();
        var kim = McpTestTools.Surface.Create(sessionId, "Agent", """{"Handle":"kim"}""");
        await Assert.That(kim.Ok).IsTrue();

        var exported = McpTestTools.Surface.Export(sessionId, "csharp");
        await Assert.That(exported.Success).IsTrue();

        var kimId = kim.InstanceId;
        var relabel = McpTestTools.Surface.Invoke(sessionId, kimId, "Relabel", """{"handle":"sam"}""");
        await Assert.That(relabel.Ok).IsFalse()
            .Because("an assign to a unique property must stay checked after an export");
        await Assert.That(relabel.Summary).Contains("Unique constraint");
    }

    /// <summary>
    /// Exporting twice around a live executor is what swapped the cached analysis at HEAD: the
    /// second <c>Emit</c> bound a session whose analysis lacked the overlay's constraint anchors.
    /// </summary>
    [Test]
    public async Task Create_DuplicateUnique_AfterTwoExportsAroundACreate_StillFails() {
        var (sessionId, _) = McpTestTools.Store.Create("ExportUnique");
        var applied = McpTestTools.Surface.Define(sessionId, Dsl);
        await Assert.That(applied.Success).IsTrue();

        await Assert.That(McpTestTools.Surface.Export(sessionId, "csharp").Success).IsTrue();
        var first = McpTestTools.Surface.Create(sessionId, "Agent", """{"Handle":"sam"}""");
        await Assert.That(first.Ok).IsTrue();
        await Assert.That(McpTestTools.Surface.Export(sessionId, "csharp").Success).IsTrue();

        var duplicate = McpTestTools.Surface.Create(sessionId, "Agent", """{"Handle":"sam"}""");
        await Assert.That(duplicate.Ok).IsFalse()
            .Because("two exports around a create must not leave the executor on a lower without the unique check");
        await Assert.That(duplicate.Summary).Contains("Unique constraint");
    }

    /// <summary>
    /// The export prints the module the executor runs. Before #387 it printed the authored
    /// session's lowering, which for a domain without <c>uses persistence</c> had no
    /// <c>unique</c> guard — so the printed C# disagreed with the runtime, and the export
    /// also evicted the running module. The invariant worth pinning: the export must not
    /// depend on whether the source wrote <c>uses persistence</c>.
    /// </summary>
    [Test]
    public async Task Export_PrintsTheSameUniqueGuardWhetherOrNotPersistenceIsAuthored() {
        const string Body = """
            Agent: entity {
              Handle: Text unique
            }
            """;

        var (implicitId, _) = McpTestTools.Store.Create("Alpha");
        await Assert.That(McpTestTools.Surface.Define(implicitId, $"domain Alpha\n{Body}").Success).IsTrue();

        var (explicitId, _) = McpTestTools.Store.Create("Beta");
        await Assert.That(McpTestTools.Surface.Define(explicitId, $"domain Beta\nuses persistence\n{Body}").Success).IsTrue();

        var implicitExport = McpTestTools.Surface.Export(implicitId, "csharp");
        var explicitExport = McpTestTools.Surface.Export(explicitId, "csharp");
        await Assert.That(implicitExport.Success).IsTrue();
        await Assert.That(explicitExport.Success).IsTrue();

        var implicitSource = implicitExport.Content;
        var explicitSource = explicitExport.Content;

        await Assert.That(implicitSource).Contains("ExistsWithHandle")
            .Because("the printed Create must carry the module's unique guard, not a stub");
        await Assert.That(explicitSource).Contains("ExistsWithHandle");
        await Assert.That(Normalise(implicitSource)).IsEqualTo(Normalise(explicitSource))
            .Because("the MCP overlay makes `uses persistence` implicit, so the export must not depend on it");
    }

    /// <summary>
    /// The only expected difference between the two exports is the authored domain name, so only
    /// those two names are neutralised. Do not widen this: replacing a term that can appear in real
    /// generated code (a C# keyword, a member name) would hide a genuine divergence.
    /// </summary>
    private static string Normalise(string source) =>
        source.Replace("Alpha", "Domain", StringComparison.Ordinal)
              .Replace("Beta", "Domain", StringComparison.Ordinal);
}