using Poly.Mcp.Tools;

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
        var applied = McpTestTools.Dsl.ApplyDsl(sessionId, Dsl);
        await Assert.That(applied.Success).IsTrue();

        // A live executor exists before the export, which is the order that broke.
        var first = McpTestTools.Runtime.CreateInstance(sessionId, "Agent", """{"Handle":"sam"}""");
        await Assert.That(first.Success).IsTrue();

        var exported = McpTestTools.Oracle.ExportDomainToCSharp(sessionId);
        await Assert.That(exported.Success).IsTrue();

        var duplicate = McpTestTools.Runtime.CreateInstance(sessionId, "Agent", """{"Handle":"sam"}""");
        await Assert.That(duplicate.Success).IsFalse()
            .Because("exporting the C# must not disable a unique constraint the domain declared");
        await Assert.That(duplicate.Message).Contains("Unique constraint");
        await Assert.That(duplicate.Message).Contains("Handle");
    }

    [Test]
    public async Task Create_DuplicateUnique_WhenExportRanFirst_StillFails() {
        var (sessionId, _) = McpTestTools.Store.Create("ExportUnique");
        var applied = McpTestTools.Dsl.ApplyDsl(sessionId, Dsl);
        await Assert.That(applied.Success).IsTrue();

        var exported = McpTestTools.Oracle.ExportDomainToCSharp(sessionId);
        await Assert.That(exported.Success).IsTrue();

        var first = McpTestTools.Runtime.CreateInstance(sessionId, "Agent", """{"Handle":"sam"}""");
        await Assert.That(first.Success).IsTrue();

        var duplicate = McpTestTools.Runtime.CreateInstance(sessionId, "Agent", """{"Handle":"sam"}""");
        await Assert.That(duplicate.Success).IsFalse()
            .Because("an export before the executor exists must not leave it lowering without the unique check");
        await Assert.That(duplicate.Message).Contains("Unique constraint");
    }

    /// <summary>
    /// Assign-time unique is the second path that reads the same module, so it is the second place
    /// an evicted module shows up. Kept separate so a fix that only repairs create is not enough.
    /// </summary>
    [Test]
    public async Task Assign_DuplicateUnique_AfterCSharpExport_StillFails() {
        var (sessionId, _) = McpTestTools.Store.Create("ExportUnique");
        var applied = McpTestTools.Dsl.ApplyDsl(sessionId, Dsl);
        await Assert.That(applied.Success).IsTrue();

        var sam = McpTestTools.Runtime.CreateInstance(sessionId, "Agent", """{"Handle":"sam"}""");
        await Assert.That(sam.Success).IsTrue();
        var kim = McpTestTools.Runtime.CreateInstance(sessionId, "Agent", """{"Handle":"kim"}""");
        await Assert.That(kim.Success).IsTrue();

        var exported = McpTestTools.Oracle.ExportDomainToCSharp(sessionId);
        await Assert.That(exported.Success).IsTrue();

        var kimId = InstanceIdOf(kim);
        var relabel = McpTestTools.Runtime.InvokeAction(sessionId, kimId, "Relabel", """{"handle":"sam"}""");
        await Assert.That(relabel.Success).IsFalse()
            .Because("an assign to a unique property must stay checked after an export");
        await Assert.That(relabel.Message).Contains("Unique constraint");
    }

    /// <summary>
    /// Exporting twice around a live executor is what swapped the cached analysis at HEAD: the
    /// second <c>Emit</c> bound a session whose analysis lacked the overlay's constraint anchors.
    /// </summary>
    [Test]
    public async Task Create_DuplicateUnique_AfterTwoExportsAroundACreate_StillFails() {
        var (sessionId, _) = McpTestTools.Store.Create("ExportUnique");
        var applied = McpTestTools.Dsl.ApplyDsl(sessionId, Dsl);
        await Assert.That(applied.Success).IsTrue();

        await Assert.That(McpTestTools.Oracle.ExportDomainToCSharp(sessionId).Success).IsTrue();
        var first = McpTestTools.Runtime.CreateInstance(sessionId, "Agent", """{"Handle":"sam"}""");
        await Assert.That(first.Success).IsTrue();
        await Assert.That(McpTestTools.Oracle.ExportDomainToCSharp(sessionId).Success).IsTrue();

        var duplicate = McpTestTools.Runtime.CreateInstance(sessionId, "Agent", """{"Handle":"sam"}""");
        await Assert.That(duplicate.Success).IsFalse()
            .Because("two exports around a create must not leave the executor on a lower without the unique check");
        await Assert.That(duplicate.Message).Contains("Unique constraint");
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

        var (implicitId, _) = McpTestTools.Store.Create("Implicit");
        await Assert.That(McpTestTools.Dsl.ApplyDsl(implicitId, $"domain Implicit\n{Body}").Success).IsTrue();

        var (explicitId, _) = McpTestTools.Store.Create("Explicit");
        await Assert.That(McpTestTools.Dsl.ApplyDsl(explicitId, $"domain Explicit\nuses persistence\n{Body}").Success).IsTrue();

        var implicitExport = McpTestTools.Oracle.ExportDomainToCSharp(implicitId);
        var explicitExport = McpTestTools.Oracle.ExportDomainToCSharp(explicitId);
        await Assert.That(implicitExport.Success).IsTrue();
        await Assert.That(explicitExport.Success).IsTrue();

        var implicitSource = SourceOf(implicitExport);
        var explicitSource = SourceOf(explicitExport);

        await Assert.That(implicitSource).Contains("ExistsWithHandle")
            .Because("the printed Create must carry the module's unique guard, not a stub");
        await Assert.That(explicitSource).Contains("ExistsWithHandle");
        await Assert.That(Normalise(implicitSource)).IsEqualTo(Normalise(explicitSource))
            .Because("the MCP overlay makes `uses persistence` implicit, so the export must not depend on it");
    }

    private static string SourceOf(DomainToolResponse response) {
        var json = System.Text.Json.JsonSerializer.Serialize(response.Data);
        using var document = System.Text.Json.JsonDocument.Parse(json);
        return document.RootElement.GetProperty("csharp").GetString()!;
    }

    /// <summary>The domain name is the only expected difference between the two exports.</summary>
    private static string Normalise(string source) =>
        source.Replace("Implicit", "Domain", StringComparison.Ordinal)
              .Replace("Explicit", "Domain", StringComparison.Ordinal)
              .Replace("implicit", "domain", StringComparison.Ordinal)
              .Replace("explicit", "domain", StringComparison.Ordinal);

    /// <summary>The instance id the tool handed back, so the test does not depend on the payload shape.</summary>
    private static string InstanceIdOf(DomainToolResponse response) {
        var json = System.Text.Json.JsonSerializer.Serialize(response.Data);
        using var document = System.Text.Json.JsonDocument.Parse(json);
        return Find(document.RootElement)
            ?? throw new InvalidOperationException($"no instanceId in '{json}'");

        static string? Find(System.Text.Json.JsonElement element) {
            if (element.ValueKind == System.Text.Json.JsonValueKind.Object) {
                foreach (var property in element.EnumerateObject()) {
                    if (string.Equals(property.Name, "instanceId", StringComparison.Ordinal))
                        return property.Value.GetString();
                    if (Find(property.Value) is { } nested)
                        return nested;
                }
            }
            else if (element.ValueKind == System.Text.Json.JsonValueKind.Array) {
                foreach (var item in element.EnumerateArray())
                    if (Find(item) is { } nested)
                        return nested;
            }
            return null;
        }
    }
}