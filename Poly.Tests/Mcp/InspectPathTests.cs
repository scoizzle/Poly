using System.Text.Json.Nodes;

using Poly.Mcp.Surface;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.Mcp;

/// <summary>
/// M-03 (#689): <c>poly_inspect</c> resolves one typed element path (property, navigation, enum, value
/// type, function, stage action, ...) instead of guessing action/stage/policy, and reports an
/// unresolvable path as <c>ELEMENT_NOT_FOUND</c> with the reason and candidates.
/// </summary>
internal sealed class InspectPathTests {
    private const string Clinic = """
        domain Clinic
        uses temporal
        uses storage
        uses persistence

        Kind: enum { A, B }

        Money: value {
          Amount: Decimal
          Currency: Text
        }

        Customer: entity {
          Name: Text
        }

        Appt: entity {
          SlotNo: Number unique
          Note: Text optional
          customer: Customer
          Adult: policy { SlotNo > 0 }
          Book: action require Adult { assign Note to "booked" }
          Open: stage require Adult {
            Go: action { transition to Done }
          }
          Review: stage {
            Go: action { transition to Done }
          }
          Done: stage { }
        }

        function Add(a: Number, b: Number): Number = a + b
        """;

    private const string Orders = """
        domain Shop

        Order: entity {
          Total: Number
          Draft: stage { Submit: action { transition to Placed } }
          Placed: stage { }
        }
        """;

    /// <summary>Describes <paramref name="select"/> against a session-store workspace, for tests that build state through the store.</summary>
    internal static InspectOutcome Describe(string sessionId, string select) {
        McpTestTools.Store.TryGet(sessionId, out var state);
        return InspectService.Inspect(sessionId, state, select, McpTestTools.Query);
    }

    private static (PolySurfaceHarness H, string Ws) Open(string poly) {
        var h = PolySurfaceHarness.New();
        var ws = h.Open("M03");
        var define = h.Define(ws, poly);
        if (!define.Success)
            throw new InvalidOperationException(define.Summary);
        return (h, ws);
    }

    private static string Str(HarnessResult r, string key) => r.Data![key]!.GetValue<string>();

    private static List<string> Strings(JsonNode? array) =>
        (array as JsonArray ?? []).Select(n => n!.GetValue<string>()).ToList();

    [Test]
    public async Task Inspect_PropertyNavigationEnumAndStageAction_Resolve() {
        var (h, ws) = Open(Clinic);

        var property = h.Inspect(ws, "Appt.SlotNo", "full");
        var stageAction = h.Inspect(ws, "Appt.Open.Go", "full");
        var enumType = h.Inspect(ws, "Kind", "full");
        var navigation = h.Inspect(ws, "Appt.customer", "full");
        var valueType = h.Inspect(ws, "Money", "full");
        var function = h.Inspect(ws, "Add", "full");

        await Assert.That(property.Success).IsTrue().Because(property.Summary);
        await Assert.That(Str(property, "kind")).IsEqualTo("property");
        await Assert.That(Str(property, "name")).IsEqualTo("SlotNo");
        await Assert.That(Str(property, "path")).IsEqualTo("Appt.SlotNo");
        await Assert.That(Str(property, "parentEntity")).IsEqualTo("Appt");
        await Assert.That(Str(property, "type")).IsEqualTo("Number");
        await Assert.That(Str(property, "constraints")).Contains("unique");

        await Assert.That(stageAction.Success).IsTrue().Because(stageAction.Summary);
        await Assert.That(Str(stageAction, "kind")).IsEqualTo("stageAction");
        await Assert.That(Str(stageAction, "path")).IsEqualTo("Appt.Open.Go");
        await Assert.That(Str(stageAction, "stage")).IsEqualTo("Open");
        await Assert.That(stageAction.Summary).Contains("Go");

        await Assert.That(enumType.Success).IsTrue().Because(enumType.Summary);
        await Assert.That(Str(enumType, "kind")).IsEqualTo("enum");
        await Assert.That(Strings(enumType.Data!["members"])).IsEquivalentTo(new[] { "A", "B" });

        await Assert.That(navigation.Success).IsTrue().Because(navigation.Summary);
        await Assert.That(Str(navigation, "kind")).IsEqualTo("navigation");
        await Assert.That(Str(navigation, "target")).IsEqualTo("Customer");

        await Assert.That(valueType.Success).IsTrue().Because(valueType.Summary);
        await Assert.That(Str(valueType, "kind")).IsEqualTo("valueType");
        await Assert.That(Strings(valueType.Data!["properties"])).IsEquivalentTo(new[] { "Amount", "Currency" });

        await Assert.That(function.Success).IsTrue().Because(function.Summary);
        await Assert.That(Str(function, "kind")).IsEqualTo("function");
        await Assert.That(Str(function, "returnType")).IsEqualTo("Number");
        await Assert.That(Strings(function.Data!["parameters"])).IsEquivalentTo(new[] { "a", "b" });
    }

    [Test]
    public async Task Inspect_UnknownPath_ErrorNamesTheKindAndCandidates() {
        var (h, ws) = Open(Clinic);

        var r = h.Inspect(ws, "Appt.SlotNp");

        await Assert.That(r.Success).IsFalse();
        await Assert.That(r.Code).IsEqualTo("ELEMENT_NOT_FOUND");
        await Assert.That(Str(r, "reason")).IsEqualTo("unknown");
        await Assert.That(Strings(r.Data!["candidates"])).Contains("Appt.SlotNo");
        await Assert.That(r.Summary).Contains("Appt").And.Contains("SlotNp").And.Contains("Appt.SlotNo");
    }

    [Test]
    public async Task Inspect_AmbiguousPath_ListsEveryMatch() {
        var (h, ws) = Open(Clinic);

        var r = h.Inspect(ws, "Appt.Go");

        await Assert.That(r.Success).IsFalse();
        await Assert.That(r.Code).IsEqualTo("ELEMENT_NOT_FOUND");
        await Assert.That(Str(r, "reason")).IsEqualTo("ambiguous");
        await Assert.That(Strings(r.Data!["candidates"])).IsEquivalentTo(new[] { "Appt.Open.Go", "Appt.Review.Go" });
    }

    [Test]
    public async Task Inspect_NotAddressablePath_SaysSoAndNamesThePartial() {
        var (h, ws) = Open(Clinic);

        var r = h.Inspect(ws, "Money.Amount");

        await Assert.That(r.Success).IsFalse();
        await Assert.That(r.Code).IsEqualTo("ELEMENT_NOT_FOUND");
        await Assert.That(Str(r, "reason")).IsEqualTo("notAddressable");
        await Assert.That(Str(r, "partial")).IsEqualTo("Money");
    }

    [Test]
    public async Task Inspect_MalformedPath_IsElementNotFoundWithThePositionedError() {
        var (h, ws) = Open(Clinic);

        var r = h.Inspect(ws, "Appt..SlotNo");

        await Assert.That(r.Success).IsFalse();
        await Assert.That(r.Code).IsEqualTo("ELEMENT_NOT_FOUND");
        await Assert.That(r.Summary).Contains("position");
    }

    [Test]
    public async Task Inspect_NotFoundInTheDefaultBrief_StillCarriesTheCandidates() {
        var (h, ws) = Open(Clinic);

        var r = h.Inspect(ws, "Appt.SlotNp", detail: null);

        await Assert.That(Strings(r.Data!["candidates"])).Contains("Appt.SlotNo");
        await Assert.That(r.Data!["description"]).IsNull();
    }

    [Test]
    public async Task Inspect_DomainPrefixedUnknownEntity_IsUnknownWithCandidates() {
        var (h, ws) = Open(Clinic);

        var r = h.Inspect(ws, "Clinic/Aptt");

        await Assert.That(r.Code).IsEqualTo("ELEMENT_NOT_FOUND");
        await Assert.That(Str(r, "reason")).IsEqualTo("unknown");
        await Assert.That(Strings(r.Data!["candidates"])).Contains("Appt");
    }

    [Test]
    public async Task Inspect_EntityActionAndSameNamedStageAction_IsAmbiguous_EachCandidateInspects() {
        var (h, ws) = Open("""
            domain Shop

            Order: entity {
              Total: Number
              Submit: action { assign Total to 1 }
              Draft: stage { Submit: action { transition to Placed } }
              Placed: stage { }
            }
            """);

        var r = h.Inspect(ws, "Order.Submit");

        await Assert.That(r.Code).IsEqualTo("ELEMENT_NOT_FOUND");
        await Assert.That(Str(r, "reason")).IsEqualTo("ambiguous");
        var candidates = Strings(r.Data!["candidates"]);
        await Assert.That(candidates.Distinct().Count()).IsEqualTo(2);
        var matches = r.Data!["matches"]!.AsArray().Select(m => m!["kind"]!.GetValue<string>()).ToList();
        await Assert.That(matches).IsEquivalentTo(new[] { "entityAction", "stageAction" });
        foreach (var candidate in candidates) {
            var each = h.Inspect(ws, candidate, "full");
            await Assert.That(each.Success).IsTrue().Because(candidate + ": " + each.Summary);
            await Assert.That(Str(each, "kind")).IsEqualTo(candidate.Contains("Draft") ? "stageAction" : "entityAction");
        }
    }

    [Test]
    public async Task Inspect_AmbiguousAcrossKindsWithOneCanonical_ListsDistinguishableMatches() {
        var (h, ws) = Open(Clinic);
        h.Store.TryGet(ws, out var state);
        var gadget = new Poly.DomainModeling.Ontology.Entity("Gadget",
            [new Poly.DomainModeling.Ontology.Property("Flag", new Poly.DomainModeling.Ontology.DomainTypeReference("Text"), [])], [], [],
            [new Poly.DomainModeling.Ontology.Stage("Flag", [], [], [], [])]);
        var domain = state!.Domain with { Types = [.. state.Domain.Types, gadget] };

        var outcome = InspectService.Inspect(ws, state with { Domain = domain }, "Gadget.Flag", McpTestTools.Query);

        await Assert.That(outcome.ErrorCode).IsEqualTo("ELEMENT_NOT_FOUND");
        var data = (ElementNotFoundData)outcome.Data!;
        await Assert.That(data.Reason).IsEqualTo("ambiguous");
        await Assert.That(data.Candidates.Distinct().Count()).IsEqualTo(2);
        await Assert.That(data.Candidates).Contains("property Gadget.Flag").And.Contains("stage Gadget.Flag");
        await Assert.That(outcome.Summary).DoesNotContain("full path");
    }

    [Test]
    public async Task Define_StageSharingAPropertyName_IsAccepted_SoInspectCanBeAmbiguous() {
        var h = PolySurfaceHarness.New();
        var ws = h.Open("M03");

        var define = h.Define(ws, "domain Shop\n\nGadget: entity {\n  Flag: Text\n  Flag: stage { }\n}\n");

        await Assert.That(define.Success).IsTrue().Because(define.Summary);
        var r = h.Inspect(ws, "Gadget.Flag");
        await Assert.That(Str(r, "reason")).IsEqualTo("ambiguous");
        await Assert.That(Strings(r.Data!["candidates"]).Distinct().Count()).IsEqualTo(2);
    }

    [Test]
    public async Task Inspect_MalformedPath_SaysThePathIsMalformed() {
        var (h, ws) = Open(Clinic);

        var r = h.Inspect(ws, "Appt..SlotNo");

        await Assert.That(r.Summary).Contains("malformed");
    }

    [Test]
    public async Task Inspect_EntityStageAction_AcceptsTheSamePathAsEdit() {
        var (h, ws) = Open(Orders);

        var bare = h.Inspect(ws, "Order.Submit", "full");
        var staged = h.Inspect(ws, "Order.Draft.Submit", "full");

        await Assert.That(staged.Success).IsTrue().Because(staged.Summary);
        await Assert.That(bare.Success).IsTrue().Because(bare.Summary);
        await Assert.That(Str(staged, "path")).IsEqualTo("Order.Draft.Submit");
        await Assert.That(Str(bare, "path")).IsEqualTo("Order.Draft.Submit");
    }

    [Test]
    public async Task Inspect_PolicyUsedByAStageAndAnAction_ResolvesToTheEntityDefinition() {
        var (h, ws) = Open(Clinic);

        var r = h.Inspect(ws, "Appt.Adult", "full");

        await Assert.That(r.Success).IsTrue().Because(r.Summary);
        await Assert.That(Str(r, "kind")).IsEqualTo("policy");
        await Assert.That(Str(r, "path")).IsEqualTo("Appt.Adult");
        await Assert.That(r.Summary).Contains("entity scope");
    }

    // ── Behaviour preserved (green at birth) ───────────────────

    [Test]
    public async Task Inspect_BriefMemberSelect_ReturnsTheDescriptionOnly() {
        var (h, ws) = Open(Clinic);

        var r = h.Inspect(ws, "Appt.Book");

        await Assert.That(r.Success).IsTrue().Because(r.Summary);
        var keys = r.Data!.AsObject().Select(kv => kv.Key).ToList();
        await Assert.That(keys).IsEquivalentTo(new[] { "description" });
    }

    [Test]
    public async Task Inspect_Overview_StillReportsTheEntityCount() {
        var (h, ws) = Open(Clinic);

        var r = h.Inspect(ws);

        await Assert.That(r.Success).IsTrue().Because(r.Summary);
        await Assert.That(r.Data!["entityCount"]!.GetValue<int>()).IsEqualTo(2);
    }

    [Test]
    public async Task Inspect_IsReadOnly_TheRevisionDoesNotMove() {
        var (h, ws) = Open(Clinic);
        var before = h.Inspect(ws).Revision;

        h.Inspect(ws, "Appt.SlotNo", "full");
        h.Inspect(ws, "Appt.Nope");

        await Assert.That(h.Inspect(ws).Revision).IsEqualTo(before);
    }

    [Test]
    public async Task Inspect_NotFound_CarriesNextHints() {
        var (h, ws) = Open(Clinic);

        var r = h.Inspect(ws, "Appt.Nope");

        await Assert.That(r.Next).IsNotEmpty();
    }

    [Test]
    public async Task Inspect_UnknownWorkspace_IsWorkspaceRequired() {
        var h = PolySurfaceHarness.New();

        var r = h.Inspect("nope", "Appt");

        await Assert.That(r.Code).IsEqualTo("WORKSPACE_REQUIRED");
    }
}