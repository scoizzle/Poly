namespace Poly.Tests.Mcp;

/// <summary>
/// #598: an export larger than the inline limit is content-addressed, so repeating it reuses one
/// file, and closing the workspace removes the files it wrote. A long-lived session could otherwise
/// grow the temp directory without bound, one file per call.
/// </summary>
public class ExportTempFileTests {
    private static string BigDocument() =>
        "domain ExportTemp\n" + string.Concat(Enumerable.Range(0, 60).Select(i =>
            $"Entity{i}: entity {{\n  Name{i}: Text\n  Total{i}: Number range(0, 100)\n}}\n"));

    private static (PolySurfaceHarness Harness, string Workspace) Workspace() {
        var harness = PolySurfaceHarness.New();
        var workspace = harness.Open("ExportTemp");
        var defined = harness.Define(workspace, BigDocument());
        if (!defined.Success)
            throw new InvalidOperationException(defined.Summary);
        return (harness, workspace);
    }

    /// <summary>Repeating an unchanged export reuses the same sha-named file.</summary>
    [Test]
    public async Task RepeatedExportOfTheSameModel_WritesOneFile() {
        var (harness, workspace) = Workspace();

        var first = harness.Export(workspace, "csharp", inline: false);
        var second = harness.Export(workspace, "csharp", inline: false);

        await Assert.That(first.Success).IsTrue().Because(first.Summary);
        await Assert.That(second.Success).IsTrue().Because(second.Summary);
        var path = first.Data!["path"]!.GetValue<string>();
        await Assert.That(second.Data!["path"]!.GetValue<string>()).IsEqualTo(path);
        await Assert.That(File.Exists(path)).IsTrue();
        await Assert.That(first.Data!["sha256"]!.GetValue<string>()).IsEqualTo(second.Data!["sha256"]!.GetValue<string>());
    }

    /// <summary>Closing the workspace removes the artifact files it wrote.</summary>
    [Test]
    public async Task ClosingTheWorkspace_RemovesItsExportedFiles() {
        var (harness, workspace) = Workspace();
        var exported = harness.Export(workspace, "csharp", inline: false);
        var path = exported.Data!["path"]!.GetValue<string>();
        await Assert.That(File.Exists(path)).IsTrue();

        var closed = harness.Workspace(op: "close", workspace: workspace);
        await Assert.That(closed.Success).IsTrue().Because(closed.Summary);
        await Assert.That(File.Exists(path)).IsFalse();
    }

    /// <summary>A different model is different content, so it gets its own file.</summary>
    [Test]
    public async Task ADifferentModel_WritesADifferentFile() {
        var (harness, workspace) = Workspace();
        var first = harness.Export(workspace, "csharp", inline: false);
        await Assert.That(harness.Edit(workspace,
            """[{"op":"add","parent":"Entity0","dsl":"Extra: Text optional"}]""").Success).IsTrue();

        var second = harness.Export(workspace, "csharp", inline: false);

        var firstPath = first.Data!["path"]!.GetValue<string>();
        var secondPath = second.Data!["path"]!.GetValue<string>();
        await Assert.That(secondPath).IsNotEqualTo(firstPath);
        await Assert.That(File.Exists(secondPath)).IsTrue();
    }
}