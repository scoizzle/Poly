using System.ComponentModel;
using System.Reflection;

using Poly.Mcp.Surface;

namespace Poly.Tests.Mcp;

/// <summary>
/// The <c>poly_simulate</c> step contract (#536, #609). Every op's key set is published — in the
/// tool description an agent reads first and in the guide — and a key outside that set is a failed
/// step naming the valid ones, never a silent no-op or a misleading "Missing argument".
/// </summary>
public class SimulateStepContractTests {
    private const string Probe = """
        domain StepProbe
        A: entity {
          Total: Number default(0)
          Add: action (n: Number) { assign Total to Total + n }
        }
        """;

    private static (PolySurfaceHarness Harness, string Workspace) Workspace() {
        var harness = PolySurfaceHarness.New();
        var workspace = harness.Open("StepProbe");
        var defined = harness.Define(workspace, Probe);
        if (!defined.Success)
            throw new InvalidOperationException(defined.Summary);
        return (harness, workspace);
    }

    /// <summary>#536: the documented key is <c>args</c>; <c>arguments</c> is a caller error on that step.</summary>
    [Test]
    public async Task Invoke_UnknownArgsKey_IsAFailedStepNamingArgs() {
        var (harness, workspace) = Workspace();

        var run = harness.Simulate(workspace, """
            [{"op":"create","entity":"A","properties":{},"as":"a"},
             {"op":"invoke","instanceId":"a","action":"Add","arguments":{"n":5}}]
            """);

        await Assert.That(run.Step(0).Ok).IsTrue().Because(run.Step(0).Summary);
        await Assert.That(run.Step(1).Ok).IsFalse();
        await Assert.That(run.Step(1).Summary).Contains("'arguments'");
        await Assert.That(run.Step(1).Summary).Contains("args");
        await Assert.That(run.Step(1).Summary).DoesNotContain("Missing argument");
    }

    /// <summary>#536: a misspelled key on create or get used to be dropped and the scenario still "succeeded".</summary>
    [Test]
    public async Task UnknownKeyOnCreateOrGet_IsAFailedStep_NotASilentSuccess() {
        var (harness, workspace) = Workspace();

        var createTypo = harness.Simulate(workspace, """
            [{"op":"create","entity":"A","properties":{},"bogusfield":1}]
            """);
        await Assert.That(createTypo.Step(0).Ok).IsFalse();
        await Assert.That(createTypo.Step(0).Summary).Contains("'bogusfield'");
        await Assert.That(createTypo.Step(0).Summary).Contains("entity");
        await Assert.That(createTypo.Step(0).Summary).Contains("properties");

        var created = harness.Create(workspace, "A");
        await Assert.That(created.Ok).IsTrue().Because(created.Summary);
        var getTypo = harness.Simulate(workspace, $$"""
            [{"op":"get","instanceId":"{{created.InstanceId}}","zzz":1}]
            """);
        await Assert.That(getTypo.Step(0).Ok).IsFalse();
        await Assert.That(getTypo.Step(0).Summary).Contains("'zzz'");
        await Assert.That(getTypo.Step(0).Summary).Contains("instanceId");
    }

    /// <summary>#609: the field names are in the description an agent reads before its first call.</summary>
    [Test]
    public async Task ToolDescription_NamesEveryOpAndItsFields() {
        var description = typeof(PolySurfaceTools)
            .GetMethod(nameof(PolySurfaceTools.Simulate))!
            .GetCustomAttribute<DescriptionAttribute>()!
            .Description;

        foreach (var fragment in new[] {
            "create", "entity", "properties", "as",
            "link", "sourceInstanceId", "relationship", "targetInstanceId",
            "unlink",
            "invoke", "instanceId", "action", "args",
            "evaluate", "policy",
            "get",
            "list",
        })
            await Assert.That(description).Contains(fragment);
    }

    /// <summary>#609: the same contract has a guide section, reachable by number or title.</summary>
    [Test]
    public async Task Guide_HasASimulateSection_ListingEveryOpAndItsFields() {
        var harness = PolySurfaceHarness.New();
        var byTitle = harness.Guide("Simulating a scenario");
        await Assert.That(byTitle.Success).IsTrue().Because(byTitle.Summary);

        var text = byTitle.Data!["guide"]!.GetValue<string>();
        foreach (var fragment in new[] {
            "create", "properties",
            "link", "sourceInstanceId", "targetInstanceId", "relationship",
            "invoke", "action", "args",
            "evaluate", "policy",
            "instanceId",
        })
            await Assert.That(text).Contains(fragment);
    }
}