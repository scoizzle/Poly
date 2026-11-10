using System.Text.Json;

using Microsoft.Extensions.Logging.Abstractions;

using Poly.DomainModeling.Language;
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

    private const string EntityThenEnumDoc = """
        domain Shop

        Zebra: entity {
          Total: Number
        }

        Alpha: entity {
          Name: Text
        }

        Size: enum { Small, Large }
        """;

    private const string UsedAmount = """
        domain Shop
        Item: entity {
          Amount: Decimal
          Bump: action { assign Amount to Amount + 1 }
        }
        """;

    private const string MarkedDoc = """
        domain Shop

        public value Money {
          Amount: Number
        }

        public entity Order {
          Total: Number
          public action Pay {
          }
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
    public async Task Add_MarkedTopLevelEntity_PlacedAfterLastEntity_NotAfterLastRoot() {
        var surface = Surface();
        var workspace = Defined(surface, EntityThenEnumDoc);

        var response = surface.Define(workspace, edit: Edit(Add("", "public entity Beta {\n  Qty: Number\n}")));

        await Assert.That(response.Ok).IsTrue();
        await Assert.That(State(workspace).SourceText)
            .IsEqualTo(EntityThenEnumDoc.Replace(
                "}\n\nSize: enum",
                "}\n\npublic entity Beta {\n  Qty: Number\n}\n\nSize: enum"));
    }

    [Test]
    public async Task Add_MarkedTopLevelValue_PlacedWithTheOtherValues_NotAfterLastRoot() {
        var surface = Surface();
        var workspace = Defined(surface, MarkedDoc);

        var response = surface.Define(workspace, edit: Edit(Add("", "public value Fee {\n  Amount: Number\n}")));

        await Assert.That(response.Ok).IsTrue();
        await Assert.That(State(workspace).SourceText)
            .IsEqualTo(MarkedDoc.Replace(
                "}\n\npublic entity Order",
                "}\n\npublic value Fee {\n  Amount: Number\n}\n\npublic entity Order"));
    }

    [Test]
    public async Task Add_MarkedTopLevelEntity_CollisionIsDetected() {
        var surface = Surface();
        var workspace = Defined(surface, MarkedDoc);

        var response = surface.Define(workspace, edit: Edit(Add("", "public entity Order {\n  Extra: Text\n}")));

        await Assert.That(response.Ok).IsFalse();
        await Assert.That(response.ErrorCode).IsEqualTo("EDIT_NAME_COLLISION");
        await Assert.That(State(workspace).Revision).IsEqualTo(1);
        await Assert.That(State(workspace).SourceText).IsEqualTo(MarkedDoc);
    }

    [Test]
    public async Task Add_MarkedTopLevelValue_CollisionIsDetected() {
        var surface = Surface();
        var workspace = Defined(surface, MarkedDoc);

        var response = surface.Define(workspace, edit: Edit(Add("", "public value Money {\n  Amount: Number\n}")));

        await Assert.That(response.Ok).IsFalse();
        await Assert.That(response.ErrorCode).IsEqualTo("EDIT_NAME_COLLISION");
        await Assert.That(State(workspace).Revision).IsEqualTo(1);
        await Assert.That(State(workspace).SourceText).IsEqualTo(MarkedDoc);
    }

    [Test]
    public async Task Apply_MarkedTopLevelAdd_ReportsDeclaredNameAsPath() {
        var outcome = PolyEdit.Apply(MarkedDoc, Edit(Add("", "public entity Customer {\n  Name: Text\n}")));

        await Assert.That(outcome.Ok).IsTrue();
        await Assert.That(outcome.Applied[0].Path).IsEqualTo("Customer");
    }

    [Test]
    public async Task Add_MarkedEntity_RoundTripsMarkedDocumentApartFromTheAdd() {
        var surface = Surface();
        var workspace = Defined(surface, MarkedDoc);

        var response = surface.Define(workspace, edit: Edit(Add("", "public entity Customer {\n  Name: Text\n}")));

        await Assert.That(response.Ok).IsTrue();
        await Assert.That(State(workspace).SourceText)
            .IsEqualTo(MarkedDoc + "\n\npublic entity Customer {\n  Name: Text\n}");

        var handWritten = Defined(surface, MarkedDoc + "\n\npublic entity Customer {\n  Name: Text\n}");

        static string Exported(string workspace) =>
            JsonSerializer.SerializeToNode(McpTestTools.Dsl.ExportDsl(workspace).Data)!["poly"]!.GetValue<string>();

        await Assert.That(Exported(workspace)).IsEqualTo(Exported(handWritten));
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
    public async Task Edit_AddReplaceRemoveMarkedMembers() {
        var surface = Surface();
        var workspace = Defined(surface, MarkedDoc);

        // Marked action: add, replace, remove inside the marked entity.
        var actionAdded = surface.Define(workspace, edit: Edit(Add("Order", "public action Refund {\n}")));
        await Assert.That(actionAdded.Ok).IsTrue();
        await Assert.That(State(workspace).SourceText).Contains("public action Refund {");

        var actionReplaced = surface.Define(workspace,
            edit: Edit(Replace("Order.Refund", "public action Refund {\n    // reversed\n  }")));
        await Assert.That(actionReplaced.Ok).IsTrue();
        await Assert.That(State(workspace).SourceText).Contains("// reversed");

        var actionRemoved = surface.Define(workspace, edit: Edit(Remove("Order.Refund")));
        await Assert.That(actionRemoved.Ok).IsTrue();
        await Assert.That(State(workspace).SourceText).DoesNotContain("Refund");

        // Marked value type: add, replace, remove at top level.
        var valueAdded = surface.Define(workspace, edit: Edit(Add("", "public value Fee {\n  Amount: Number\n}")));
        await Assert.That(valueAdded.Ok).IsTrue();
        await Assert.That(State(workspace).SourceText).Contains("public value Fee {");

        var valueReplaced = surface.Define(workspace,
            edit: Edit(Replace("Fee", "public value Fee {\n  Amount: Decimal\n}")));
        await Assert.That(valueReplaced.Ok).IsTrue();
        await Assert.That(State(workspace).SourceText).Contains("public value Fee {\n  Amount: Decimal");

        var valueRemoved = surface.Define(workspace, edit: Edit(Remove("Fee")));
        await Assert.That(valueRemoved.Ok).IsTrue();
        await Assert.That(State(workspace).SourceText).DoesNotContain("Fee");

        // Marked entity: add, replace, remove at top level.
        var entityAdded = surface.Define(workspace, edit: Edit(Add("", "public entity Customer {\n  Name: Text\n}")));
        await Assert.That(entityAdded.Ok).IsTrue();
        await Assert.That(State(workspace).SourceText).Contains("public entity Customer {");

        var entityReplaced = surface.Define(workspace,
            edit: Edit(Replace("Customer", "public entity Client {\n  Name: Text\n}")));
        await Assert.That(entityReplaced.Ok).IsTrue();
        await Assert.That(State(workspace).SourceText).Contains("public entity Client {");

        var entityRemoved = surface.Define(workspace, edit: Edit(Remove("Client")));
        await Assert.That(entityRemoved.Ok).IsTrue();
        await Assert.That(State(workspace).SourceText!.TrimEnd()).IsEqualTo(MarkedDoc.TrimEnd());
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