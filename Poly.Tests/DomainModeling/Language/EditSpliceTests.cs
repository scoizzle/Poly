using System.Text.Json;

using Microsoft.Extensions.Logging.Abstractions;

using Poly.DomainModeling.Language;
using Poly.Mcp.Sessions;
using Poly.Mcp.Surface;

using static Poly.Tests.TestHelpers.ContractContributions;

namespace Poly.Tests.DomainModeling.Language;

/// <summary>
/// G-08 (#613, #646): the <c>dsl</c> of an <c>add</c>/<c>replace</c> op must be exactly one
/// balanced declaration of the kind its target takes, <c>uses</c> lines are addressable as
/// <c>uses:&lt;pack&gt;</c>, and removing the document's last uses line fails closed.
/// </summary>
public class EditSpliceTests {
    private const string Doc = """
        domain Shop
        uses storage
        uses http
        Order: entity {
          Total: Number
        }
        """;

    /// <summary>A payload that closes the parent brace and smuggles a top-level entity in.</summary>
    private const string BraceBreakout = "X: Number }\nEvil: entity {\n  Y: Number";

    private static string Edit(params string[] ops) => "[" + string.Join(",", ops) + "]";

    private static string Add(string parent, string dsl) =>
        JsonSerializer.Serialize(new { op = "add", parent, dsl });

    private static string Remove(string path) => JsonSerializer.Serialize(new { op = "remove", path });

    private static string Replace(string path, string dsl) =>
        JsonSerializer.Serialize(new { op = "replace", path, dsl });

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

    private static AppliedEdit Applied(PolyResponse response) =>
        ((IReadOnlyList<AppliedEdit>)((Dictionary<string, object?>)response.Data!)["applied"]!)[0];

    // ── B1: one balanced declaration per dsl ────────────────────

    [Test]
    public async Task Add_DslWithClosingBrace_IsRejectedAndModelUnchanged() {
        var surface = Surface();
        var workspace = Defined(surface);

        var response = surface.Define(workspace, edit: Edit(Add("Order", BraceBreakout)));

        await Assert.That(response.Ok).IsFalse();
        await Assert.That(response.ErrorCode).IsEqualTo("EDIT_BAD_SNIPPET");
        await Assert.That(response.Summary).Contains("unmatched '}'");
        await Assert.That(State(workspace).Revision).IsEqualTo(1);
        await Assert.That(State(workspace).SourceText).IsEqualTo(Doc);
    }

    [Test]
    public async Task Add_TwoMembersInOneOp_Rejected() {
        var surface = Surface();
        var workspace = Defined(surface);

        var response = surface.Define(workspace, edit: Edit(Add("Order", "A: Number\nB: Number")));

        await Assert.That(response.Ok).IsFalse();
        await Assert.That(response.ErrorCode).IsEqualTo("EDIT_BAD_SNIPPET");
        await Assert.That(response.Summary).Contains("more than one declaration");
        await Assert.That(State(workspace).Revision).IsEqualTo(1);
        await Assert.That(State(workspace).SourceText).IsEqualTo(Doc);
    }

    [Test]
    public async Task Add_TwoTopLevelDeclarations_Rejected() {
        var surface = Surface();
        var workspace = Defined(surface);

        var response = surface.Define(workspace, edit: Edit(Add("", """
            A1: entity {
              X: Number
            }
            A2: entity {
              Y: Number
            }
            """)));

        await Assert.That(response.Ok).IsFalse();
        await Assert.That(response.ErrorCode).IsEqualTo("EDIT_BAD_SNIPPET");
        await Assert.That(response.Summary).Contains("more than one declaration");
        await Assert.That(State(workspace).Revision).IsEqualTo(1);
        await Assert.That(State(workspace).SourceText).IsEqualTo(Doc);
    }

    [Test]
    public async Task Replace_PropertyEmittingBrace_Rejected() {
        var surface = Surface();
        var workspace = Defined(surface);

        var response = surface.Define(workspace, edit: Edit(Replace("Order.Total", "Total: Number }\nEvil: entity {\n  Y: Number")));

        await Assert.That(response.Ok).IsFalse();
        await Assert.That(response.ErrorCode).IsEqualTo("EDIT_BAD_SNIPPET");
        await Assert.That(response.Summary).Contains("unmatched '}'");
        await Assert.That(State(workspace).Revision).IsEqualTo(1);
        await Assert.That(State(workspace).SourceText).IsEqualTo(Doc);
    }

    [Test]
    public async Task Replace_UnclosedBrace_Rejected() {
        var outcome = PolyEdit.Apply(Doc, Edit(Replace("Order.Total", "Total: entity {\n  Y: Number")));

        await Assert.That(outcome.Ok).IsFalse();
        await Assert.That(outcome.ErrorCode).IsEqualTo("EDIT_BAD_SNIPPET");
        await Assert.That(outcome.Error!).Contains("unclosed '{");
        await Assert.That(outcome.Text).IsNull();
    }

    [Test]
    public async Task Edit_SecondOpBadSnippet_IsAtomic() {
        var surface = Surface();
        var workspace = Defined(surface);

        var response = surface.Define(workspace, edit: Edit(
            Add("Order", "Number: Number"),
            Add("Order", BraceBreakout)));

        await Assert.That(response.Ok).IsFalse();
        await Assert.That(response.ErrorCode).IsEqualTo("EDIT_BAD_SNIPPET");
        await Assert.That(State(workspace).Revision).IsEqualTo(1);
        await Assert.That(State(workspace).SourceText).IsEqualTo(Doc);
    }

    [Test]
    public async Task Add_CommentOnlyDsl_IsRejected() {
        var outcome = PolyEdit.Apply(Doc, Edit(Add("Order", "// note")));

        await Assert.That(outcome.Ok).IsFalse();
        await Assert.That(outcome.ErrorCode).IsEqualTo("EDIT_BAD_SNIPPET");
        await Assert.That(outcome.Error!).Contains("does not declare a member");
        await Assert.That(outcome.Text).IsNull();
    }

    // Guards: a legal fragment must stay accepted (green before and after the shape check).

    [Test]
    public async Task Add_HeaderRequireAction_IsAccepted() {
        var outcome = PolyEdit.Apply(Doc, Edit(Add("Order",
            "CheckOut: action (book: Book)\n  require GoodStanding\n  require not AtLimit\n{\n}")));

        await Assert.That(outcome.Ok).IsTrue();
    }

    [Test]
    public async Task Add_StageHeaderRequire_IsAccepted() {
        var outcome = PolyEdit.Apply(Doc, Edit(Add("Order", "Open: stage require HasNote { }")));

        await Assert.That(outcome.Ok).IsTrue();
    }

    [Test]
    public async Task Add_PrimitiveNamedMember_IsAccepted() {
        var outcome = PolyEdit.Apply(Doc, Edit(Add("Order", "Number: Number")));

        await Assert.That(outcome.Ok).IsTrue();
    }

    // ── B2: uses:<pack> is an addressable root ──────────────────

    [Test]
    public async Task Remove_UsesStorage_ByUsesPath_RemovesTheLine() {
        var surface = Surface();
        var workspace = Defined(surface);

        var response = surface.Define(workspace, edit: Edit(Remove("uses:storage")));

        await Assert.That(response.Ok).IsTrue();
        await Assert.That(State(workspace).SourceText).IsEqualTo(Doc.Replace("uses storage\n", ""));
        await Assert.That(Applied(response).Path).IsEqualTo("uses:storage");
        await Assert.That(State(workspace).Revision).IsEqualTo(2);
    }

    [Test]
    public async Task Remove_UsesWithVersion_ByUsesPath_RemovesTheWholeLine() {
        const string Versioned = """
            domain Shop
            uses storage v1
            uses http
            Order: entity {
              Total: Number
            }
            """;

        var outcome = PolyEdit.Apply(Versioned, Edit(Remove("uses:storage")));

        await Assert.That(outcome.Ok).IsTrue();
        await Assert.That(outcome.Text).IsEqualTo(Versioned.Replace("uses storage v1\n", ""));
        await Assert.That(outcome.Text!).DoesNotContain("v1");
    }

    [Test]
    public async Task Remove_LastUsesLine_Rejected() {
        const string OnlyUses = """
            domain Shop
            uses storage
            Order: entity {
              Total: Number
            }
            """;
        var surface = Surface();
        var workspace = Defined(surface, OnlyUses);

        var response = surface.Define(workspace, edit: Edit(Remove("uses:storage")));

        await Assert.That(response.Ok).IsFalse();
        await Assert.That(response.ErrorCode).IsEqualTo("EDIT_NOT_ADDRESSABLE");
        await Assert.That(response.Summary).Contains("only uses line");
        await Assert.That(State(workspace).Revision).IsEqualTo(1);
        await Assert.That(State(workspace).SourceText).IsEqualTo(OnlyUses);
    }

    [Test]
    public async Task Remove_UnknownUses_NearMissListsUses() {
        var typo = PolyEdit.Apply(Doc, Edit(Remove("uses:strge")));
        var bare = PolyEdit.Apply(Doc, Edit(Remove("uses")));

        await Assert.That(typo.ErrorCode).IsEqualTo("EDIT_PATH_NOT_FOUND");
        await Assert.That(typo.Error!).Contains("uses:storage");
        await Assert.That(bare.ErrorCode).IsEqualTo("EDIT_PATH_NOT_FOUND");
        await Assert.That(bare.Error!).Contains("uses:storage");
    }

    [Test]
    public async Task Replace_UsesPath_IsAddressable() {
        var outcome = PolyEdit.Apply(Doc, Edit(Replace("uses:storage", "uses storage")));

        await Assert.That(outcome.Ok).IsTrue();
        await Assert.That(outcome.Text).IsEqualTo(Doc);
    }

    [Test]
    public async Task Add_TopLevelUses_ReportsUsesPath() {
        const string Versioned = """
            domain Shop
            uses storage v1
            Order: entity {
              Total: Number
            }
            """;

        var outcome = PolyEdit.Apply(Versioned, Edit(Add("", "uses http")));

        await Assert.That(outcome.Ok).IsTrue();
        await Assert.That(outcome.Applied[0].Path).IsEqualTo("uses:http");
        await Assert.That(outcome.Text).Contains("uses storage v1\nuses http");
    }

    [Test]
    public async Task Replace_MemberSnippetOnUsesPath_Rejected() {
        var outcome = PolyEdit.Apply(Doc,
            Edit(Replace("uses:storage", "Order: entity {\n  Total: Number\n}")));

        await Assert.That(outcome.Ok).IsFalse();
        await Assert.That(outcome.ErrorCode).IsEqualTo("EDIT_NOT_ADDRESSABLE");
        await Assert.That(outcome.Error!).Contains("must be a uses line");
        await Assert.That(outcome.Error!).Contains("dsl line 1, col 1");
    }

    [Test]
    public async Task Replace_MemberWithUsesSnippet_Rejected() {
        var outcome = PolyEdit.Apply(Doc, Edit(Replace("Order.Total", "uses storage")));

        await Assert.That(outcome.Ok).IsFalse();
        await Assert.That(outcome.ErrorCode).IsEqualTo("EDIT_NOT_ADDRESSABLE");
        await Assert.That(outcome.Error!).Contains("must not be a uses line");
        await Assert.That(outcome.Error!).Contains("dsl line 1, col 1");
    }

    [Test]
    public async Task Add_UsesSnippetWithParent_Rejected() {
        var outcome = PolyEdit.Apply(Doc, Edit(Add("Order", "uses storage")));

        await Assert.That(outcome.Ok).IsFalse();
        await Assert.That(outcome.ErrorCode).IsEqualTo("EDIT_NOT_ADDRESSABLE");
        await Assert.That(outcome.Error!).Contains("top-level");
        await Assert.That(outcome.Error!).Contains("dsl line 1, col 1");
    }

    // ── B2: BOM documents ───────────────────────────────────────

    [Test]
    public async Task Edit_OnBomDocument_RemoveUses_KeepsBomAtZero() {
        const string Bom = "\uFEFF";
        const string BomDoc = Bom + "domain Shop\nuses storage\nuses http\nOrder: entity {\n  Total: Number\n}";

        var outcome = PolyEdit.Apply(BomDoc, Edit(Remove("uses:storage")));

        await Assert.That(outcome.Ok).IsTrue();
        await Assert.That(outcome.Text!.StartsWith(Bom, StringComparison.Ordinal)).IsTrue();
        await Assert.That(outcome.Text)
            .IsEqualTo(Bom + "domain Shop\nuses http\nOrder: entity {\n  Total: Number\n}");
    }

    [Test]
    public async Task Edit_OnBomDocument_BadSnippet_IsRejectedAndTextUnchanged() {
        const string Bom = "\uFEFF";

        var outcome = PolyEdit.Apply(Bom + Doc, Edit(Add("Order", BraceBreakout)));

        await Assert.That(outcome.Ok).IsFalse();
        await Assert.That(outcome.ErrorCode).IsEqualTo("EDIT_BAD_SNIPPET");
        await Assert.That(outcome.Text).IsNull();
    }

    [Test]
    public async Task Inspect_BomFragments_ReportSnippetRelativePositions() {
        // A leading BOM is an encoding artifact: it moves no line or column, only the offset.
        var leading = DslOutline.Inspect("\uFEFFX: Number }", topLevel: false);
        await Assert.That(leading.Fault!.Kind).IsEqualTo(SnippetFaultKind.UnexpectedClose);
        await Assert.That(leading.Fault!.Line).IsEqualTo(1);
        await Assert.That(leading.Fault!.Col).IsEqualTo(11);

        var midText = DslOutline.Inspect("X: Number\uFEFF", topLevel: false);
        await Assert.That(midText.Fault!.Kind).IsEqualTo(SnippetFaultKind.BadToken);
        await Assert.That(midText.Fault!.Detail!).Contains("U+FEFF");
    }

    // ── B3: dropping the uses line loses library resolution ─────

    /// <summary>
    /// The editor no longer lets a document lose its last uses line (A2), so this proves the
    /// second half: the same text with the line gone loses library resolution at the gate. It
    /// proves library resolution only; the unchanged-revision behaviour is generic and covered by
    /// <c>PolyDefineEditTests.Edit_RejectedByAnalysis_LeavesRevisionAndTextUnchanged</c>.
    /// </summary>
    [Test]
    public async Task Edit_RemoveUsesStripe_DependencyLossIsRefusedByTheGate() {
        // Copy of ContractSpliceLoweringTests' fixture; the original is a private const.
        const string StripeDsl = """
            domain Shop
            uses stripe

            Order: entity {
              Total: Number default(0)
              Pay: action (request: ChargeRequest) {
                assign Total to Total
              }
            }

            ChargeOrder: bind Stripe Charge to Pay request
            """;
        const string WithoutUses = """
            domain Shop

            Order: entity {
              Total: Number default(0)
              Pay: action (request: ChargeRequest) {
                assign Total to Total
              }
            }

            ChargeOrder: bind Stripe Charge to Pay request
            """;

        static bool GateAccepts(string poly) {
            try {
                Evolve(poly, StripeLibrary());
                return true;
            }
            catch (InvalidOperationException) {
                return false;
            }
        }

        await Assert.That(GateAccepts(StripeDsl)).IsTrue();

        var refused = PolyEdit.Apply(StripeDsl, Edit(Remove("uses:stripe")));
        await Assert.That(refused.ErrorCode).IsEqualTo("EDIT_NOT_ADDRESSABLE");

        await Assert.That(GateAccepts(WithoutUses)).IsFalse();
    }
}