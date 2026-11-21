using System.Text.Json.Nodes;

namespace Poly.Tests.Mcp;

/// <summary>
/// The <c>poly_define</c> result contract (#645, #575). One casing for the same fields whether
/// the call applied or dry-ran, and an apply that drops simulated instances says so — on the
/// summary and in <c>data.instancesCleared</c> — instead of the next <c>get</c> discovering it.
/// </summary>
public class PolyDefineResultContractTests {
    private const string Doc = """
        domain DefineResult
        Order: entity {
          Total: Number range(0, 1000) default(0)
          Note: Text optional
        }
        """;

    private static (PolySurfaceHarness Harness, string Workspace) Workspace() {
        var harness = PolySurfaceHarness.New();
        var workspace = harness.Open("DefineResult");
        var defined = harness.Define(workspace, Doc);
        if (!defined.Success)
            throw new InvalidOperationException(defined.Summary);
        return (harness, workspace);
    }

    private static string Add(string member) =>
        $$"""[{"op":"add","parent":"Order","dsl":"{{member}}"}]""";

    private static string[] Keys(JsonNode? data) =>
        (data as JsonObject)?.Select(pair => pair.Key).Order(StringComparer.Ordinal).ToArray() ?? [];

    /// <summary>#645: the dry run used to answer DomainName/Valid/EntityCount beside camelCase applied fields.</summary>
    [Test]
    public async Task DryRunAndApply_NameTheSameFields_InTheSameCasing() {
        var (harness, workspace) = Workspace();

        var dry = harness.Edit(workspace, Add("Qty: Number default(1)"), apply: false);
        await Assert.That(dry.Success).IsTrue().Because(dry.Summary);
        var dryKeys = Keys(dry.Data);
        foreach (var key in new[] { "domainName", "valid", "entityCount", "relationshipCount", "errorCount", "warningCount", "hintCount" })
            await Assert.That(dryKeys).Contains(key).Because(string.Join(", ", dryKeys));
        await Assert.That(dry.Data!.AsObject().Any(pair => char.IsUpper(pair.Key[0]))).IsFalse()
            .Because(string.Join(", ", dryKeys));

        var applied = harness.Edit(workspace, Add("Qty: Number default(1)"));
        await Assert.That(applied.Success).IsTrue().Because(applied.Summary);
        var appliedKeys = Keys(applied.Data);
        await Assert.That(appliedKeys).Contains("domainName");
        await Assert.That(appliedKeys).Contains("entityCount");
        await Assert.That(appliedKeys).Contains("revision");
    }

    /// <summary>#575: an edit that cannot affect the instances still discarded them silently.</summary>
    [Test]
    public async Task EditThatClearsInstances_SaysHowMany_AndTheModelStillApplies() {
        var (harness, workspace) = Workspace();
        var created = harness.Create(workspace, "Order", """{"Total":150}""");
        await Assert.That(created.Ok).IsTrue().Because(created.Summary);

        var edited = harness.Edit(workspace, Add("Qty: Number default(1)"));

        await Assert.That(edited.Success).IsTrue().Because(edited.Summary);
        await Assert.That(edited.Summary).Contains("1");
        await Assert.That(edited.Summary).Contains("instance");
        await Assert.That(edited.Data!["instancesCleared"]!.GetValue<int>()).IsEqualTo(1);
        await Assert.That(edited.Instances).IsEqualTo(0);
    }

    /// <summary>#575: a whole-document re-apply clears too, and reports the same way.</summary>
    [Test]
    public async Task PolyReapplyThatClearsInstances_ReportsTheCount() {
        var (harness, workspace) = Workspace();
        await Assert.That(harness.Create(workspace, "Order").Ok).IsTrue();
        await Assert.That(harness.Create(workspace, "Order").Ok).IsTrue();

        var reapplied = harness.Define(workspace, Doc);

        await Assert.That(reapplied.Success).IsTrue().Because(reapplied.Summary);
        await Assert.That(reapplied.Summary).Contains("2");
        await Assert.That(reapplied.Summary).Contains("instance");
        await Assert.That(reapplied.Data!["instancesCleared"]!.GetValue<int>()).IsEqualTo(2);
    }

    /// <summary>#575: a dry run and a rejected edit clear nothing, so they must not claim to.</summary>
    [Test]
    public async Task DryRunAndRejectedEdit_ClearNothing_AndSayNothingWasCleared() {
        var (harness, workspace) = Workspace();
        await Assert.That(harness.Create(workspace, "Order").Ok).IsTrue();

        var dry = harness.Edit(workspace, Add("Qty: Number default(1)"), apply: false);
        await Assert.That(dry.Success).IsTrue().Because(dry.Summary);
        await Assert.That(dry.Data!["instancesCleared"] is null).IsTrue();
        await Assert.That(dry.Instances).IsEqualTo(1);

        var rejected = harness.Edit(workspace, Add("Total: Text"));
        await Assert.That(rejected.Success).IsFalse();
        await Assert.That(rejected.Instances).IsEqualTo(1);
    }
}