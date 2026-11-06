using System.Text.Json;

using Microsoft.Extensions.Logging.Abstractions;

using Poly.Mcp.Sessions;
using Poly.Mcp.Surface;
using Poly.Mcp.Tools;

namespace Poly.Tests.Mcp;

public class PolyDefineEditTests {
    private const string Doc = """
        domain Shop

        // Orders come first
        Zebra: entity {
          // running total
          Total: Number range(0, 100) // sum so far
          Note: Text optional
          Draft: stage { Submit: action { transition to Active } }
          Active: stage {}
        }

        Alpha: entity {
          Name: Text
        }
        """;

    private const string UsedAmount = """
        domain Shop
        Item: entity {
          Amount: Decimal
          Bump: action { assign Amount to Amount + 1 }
        }
        """;

    private static PolySurfaceTools Surface() =>
        new(McpTestTools.Store, NullLogger<PolySurfaceTools>.Instance);

    private static string Defined(PolySurfaceTools surface, string doc = Doc) {
        var workspace = surface.Workspace(name: "Shop").State!.Workspace;
        var response = surface.Define(workspace, poly: doc);
        if (!response.Ok)
            throw new InvalidOperationException(response.Summary);
        return workspace;
    }

    private static McpSessionState State(string workspace) {
        McpTestTools.Store.TryGet(workspace, out var state);
        return state;
    }

    private static string Edit(params string[] ops) => "[" + string.Join(",", ops) + "]";

    private static string Add(string parent, string dsl) =>
        JsonSerializer.Serialize(new { op = "add", parent, dsl });

    private static string Remove(string path) => JsonSerializer.Serialize(new { op = "remove", path });

    private static string Replace(string path, string dsl) =>
        JsonSerializer.Serialize(new { op = "replace", path, dsl });

    [Test]
    public async Task Add_InsertsPropertyAndKeepsSurroundingComments() {
        var surface = Surface();
        var workspace = Defined(surface);

        var response = surface.Define(workspace, edit: Edit(Add("Zebra", "Amount: Decimal")));

        await Assert.That(response.Ok).IsTrue();
        await Assert.That(State(workspace).SourceText)
            .IsEqualTo(Doc.Replace("  Active: stage {}\n}", "  Active: stage {}\n  Amount: Decimal\n}"));
        await Assert.That(State(workspace).Revision).IsEqualTo(2);
    }

    [Test]
    public async Task Add_TopLevelEntity_PlacedAfterLastEntity() {
        var surface = Surface();
        var workspace = Defined(surface);

        var response = surface.Define(workspace, edit: Edit(Add("", "Beta: entity {\n  Qty: Number\n}")));

        await Assert.That(response.Ok).IsTrue();
        await Assert.That(State(workspace).SourceText).IsEqualTo(Doc + "\n\nBeta: entity {\n  Qty: Number\n}");
    }

    [Test]
    public async Task Add_TopLevelEnum_PlacedBeforeFirstEntity() {
        var surface = Surface();
        var workspace = Defined(surface);

        var response = surface.Define(workspace, edit: Edit(Add("", "Color: enum { Red, Green }")));

        await Assert.That(response.Ok).IsTrue();
        await Assert.That(State(workspace).SourceText)
            .IsEqualTo(Doc.Replace("domain Shop\n", "domain Shop\n\nColor: enum { Red, Green }\n"));
    }

    [Test]
    public async Task Remove_DeletesOnlyTheTargetMember() {
        var surface = Surface();
        var workspace = Defined(surface);

        var response = surface.Define(workspace, edit: Edit(Remove("Zebra.Note")));

        await Assert.That(response.Ok).IsTrue();
        await Assert.That(State(workspace).SourceText).IsEqualTo(Doc.Replace("  Note: Text optional\n", ""));
    }

    [Test]
    public async Task Replace_ChangesTypeAndKeepsTrailingComment() {
        var surface = Surface();
        var workspace = Defined(surface);

        var response = surface.Define(workspace, edit: Edit(Replace("Zebra.Total", "Total: Decimal")));

        await Assert.That(response.Ok).IsTrue();
        await Assert.That(State(workspace).SourceText)
            .IsEqualTo(Doc.Replace("Total: Number range(0, 100) // sum so far", "Total: Decimal // sum so far"));
    }

    [Test]
    public async Task Edit_PreservesAuthorOrderingAndComments() {
        var surface = Surface();
        var workspace = Defined(surface);

        surface.Define(workspace, edit: Edit(Add("Alpha", "Age: Number")));

        var source = State(workspace).SourceText!;
        await Assert.That(source.IndexOf("Zebra", StringComparison.Ordinal))
            .IsLessThan(source.IndexOf("Alpha", StringComparison.Ordinal));
        await Assert.That(source).Contains("// Orders come first");
        await Assert.That(source).Contains("// running total");
        await Assert.That(source).Contains("// sum so far");
    }

    [Test]
    public async Task Edit_RejectedByAnalysis_LeavesRevisionAndTextUnchanged() {
        var surface = Surface();
        var workspace = Defined(surface);

        var response = surface.Define(workspace,
            edit: Edit(Add("Zebra", "Raise: action { assign Total to Total + 250 }")));

        await Assert.That(response.Ok).IsFalse();
        await Assert.That(response.Findings.Count).IsGreaterThan(0);
        await Assert.That(State(workspace).Revision).IsEqualTo(1);
        await Assert.That(State(workspace).SourceText).IsEqualTo(Doc);
    }

    [Test]
    public async Task Edit_UnknownPath_FailsClosedWithNearMisses() {
        var surface = Surface();
        var workspace = Defined(surface);

        var response = surface.Define(workspace, edit: Edit(Remove("Zebra.Totl")));

        await Assert.That(response.Ok).IsFalse();
        await Assert.That(response.ErrorCode).IsEqualTo("EDIT_PATH_NOT_FOUND");
        await Assert.That(response.Summary).Contains("Zebra.Total");
        await Assert.That(State(workspace).Revision).IsEqualTo(1);
    }

    [Test]
    public async Task Edit_MultipleOps_AreAtomic_WhenOp2FailsAtTheGate() {
        var surface = Surface();
        var workspace = Defined(surface);

        var response = surface.Define(workspace, edit: Edit(
            Add("Zebra", "Amount: Decimal"),
            Add("Zebra", "Raise: action { assign Total to Total + 250 }")));

        await Assert.That(response.Ok).IsFalse();
        await Assert.That(State(workspace).Revision).IsEqualTo(1);
        await Assert.That(State(workspace).SourceText).IsEqualTo(Doc);
    }

    [Test]
    public async Task Edit_WithoutPriorDefine_FailsClosedWithEditNoSource() {
        var surface = Surface();
        var workspace = surface.Workspace(name: "Empty").State!.Workspace;

        var response = surface.Define(workspace, edit: Edit(Add("", "A: entity {\n  X: Text\n}")));

        await Assert.That(response.Ok).IsFalse();
        await Assert.That(response.ErrorCode).IsEqualTo("EDIT_NO_SOURCE");
        await Assert.That(State(workspace).Revision).IsEqualTo(0);
    }

    [Test]
    public async Task Edit_StaleRevision_IsRejected() {
        var surface = Surface();
        var workspace = Defined(surface);
        var baseRevision = State(workspace).Revision;
        var first = PolyEdit.Apply(Doc, Edit(Add("Zebra", "Amount: Decimal"))).Text!;
        var second = PolyEdit.Apply(Doc, Edit(Add("Zebra", "Weight: Decimal"))).Text!;

        var won = McpTestTools.Dsl.ApplyDslAtRevision(workspace, first, baseRevision);
        var lost = McpTestTools.Dsl.ApplyDslAtRevision(workspace, second, baseRevision);

        await Assert.That(won.Success).IsTrue();
        await Assert.That(lost.Success).IsFalse();
        await Assert.That(lost.Message).Contains("changed since");
        await Assert.That(State(workspace).SourceText).IsEqualTo(first);
        await Assert.That(State(workspace).Revision).IsEqualTo(baseRevision + 1);
    }

    [Test]
    public async Task Edit_ApplyFalse_StoresNothing() {
        var surface = Surface();
        var workspace = Defined(surface);

        var response = surface.Define(workspace, apply: false, edit: Edit(Add("Zebra", "Amount: Decimal")));

        await Assert.That(response.Ok).IsTrue();
        await Assert.That(State(workspace).Revision).IsEqualTo(1);
        await Assert.That(State(workspace).SourceText).IsEqualTo(Doc);
        var spliced = JsonSerializer.SerializeToNode(response.Data)!["spliced"]!.GetValue<string>();
        await Assert.That(spliced).Contains("Amount: Decimal");
    }

    [Test]
    public async Task Edit_RequiresExactlyOneOfPolyOrEdit() {
        var surface = Surface();
        var workspace = Defined(surface);

        var both = surface.Define(workspace, poly: Doc, edit: Edit(Remove("Zebra.Note")));
        var neither = surface.Define(workspace);

        await Assert.That(both.ErrorCode).IsEqualTo("EDIT_ARGUMENT_CONFLICT");
        await Assert.That(neither.ErrorCode).IsEqualTo("EDIT_ARGUMENT_CONFLICT");
        await Assert.That(State(workspace).Revision).IsEqualTo(1);
    }

    [Test]
    public async Task Edit_MalformedJson_FailsClosed() {
        var surface = Surface();
        var workspace = Defined(surface);

        var notJson = surface.Define(workspace, edit: "not json");
        var notArray = surface.Define(workspace, edit: "{}");
        var unknownOp = surface.Define(workspace, edit: "[{\"op\":\"rename\",\"path\":\"Zebra\"}]");

        await Assert.That(notJson.ErrorCode).IsEqualTo("EDIT_BAD_JSON");
        await Assert.That(notArray.ErrorCode).IsEqualTo("EDIT_BAD_JSON");
        await Assert.That(unknownOp.ErrorCode).IsEqualTo("EDIT_UNKNOWN_OP");
        await Assert.That(State(workspace).SourceText).IsEqualTo(Doc);
    }

    [Test]
    public async Task Edit_RemovePropertyUsedByPolicy_GateRejectsAndStateUnchanged() {
        var surface = Surface();
        var workspace = Defined(surface, UsedAmount);

        var response = surface.Define(workspace, edit: Edit(Remove("Item.Amount")));

        await Assert.That(response.Ok).IsFalse();
        await Assert.That(State(workspace).Revision).IsEqualTo(1);
        await Assert.That(State(workspace).SourceText).IsEqualTo(UsedAmount);
    }

    [Test]
    public async Task Edit_ResultMatchesHandWrittenDocument() {
        var surface = Surface();
        var edited = Defined(surface);
        surface.Define(edited, edit: Edit(Add("Zebra", "Amount: Decimal"), Remove("Zebra.Note")));

        var handWritten = Defined(surface, Doc
            .Replace("  Note: Text optional\n", "")
            .Replace("  Active: stage {}\n}", "  Active: stage {}\n  Amount: Decimal\n}"));

        static string Exported(string workspace) =>
            JsonSerializer.SerializeToNode(McpTestTools.Dsl.ExportDsl(workspace).Data)!["poly"]!.GetValue<string>();

        await Assert.That(Exported(edited)).IsEqualTo(Exported(handWritten));
        await Assert.That(State(edited).SourceText).IsEqualTo(State(handWritten).SourceText);
    }
}