using System.Text.Json.Nodes;

using Poly.Tests.TestHelpers;

namespace Poly.Tests.Mcp;

/// <summary>
/// Instance snapshot rendering (probe findings S9 / S13): a navigation property
/// reads as a usable reference, never a runtime object dump, and a stage-less
/// entity reports a real "no stage" value instead of an omitted <c>null</c>.
/// </summary>
public class InstanceRenderingTests {
    private const string NavDomain = """
        domain InstanceNavRender
        Guest: entity {
          Name: Text
          reservations: many Reservation
          Active: stage {
            Book: action (table: DiningTable, size: Number) -> Reservation {
              create in reservations {
                PartySize: size
                table: table
              }
            }
          }
        }
        DiningTable: entity {
          Code: Text
          Seats: Number
          reservations: many Reservation
        }
        Reservation: entity {
          PartySize: Number
          guest: Guest
          table: DiningTable
          Booked: stage { }
        }
        """;

    private static JsonNode SnapshotOf(HarnessStep step) =>
        step.Data?["instance"] ?? throw new InvalidOperationException($"Step {step.Index} ({step.Op}) has no instance: {step.Summary}");

    private static string CreateAndId(string sessionId, string entity, string properties) {
        var create = McpTestTools.Surface.Create(sessionId, entity, properties);
        if (!create.Ok)
            throw new InvalidOperationException($"create_instance {entity}: {create.Summary}");
        return create.InstanceId;
    }

    [Test]
    public async Task GetInstance_NavigationProperty_RendersLinkedInstanceId() {
        var sessionId = McpTestTools.Surface.Open("InstanceNavRender");
        await Assert.That(McpTestTools.Surface.Define(sessionId, NavDomain).Success).IsTrue();

        var guestId = CreateAndId(sessionId, "Guest", """{"Name":"Ada"}""");
        var tableId = CreateAndId(sessionId, "DiningTable", """{"Code":"T1","Seats":4}""");

        // Book binds the DiningTable action arg by instance id, links the table,
        // and back-references the Guest — both to-one navs land in the bag.
        var book = McpTestTools.Surface.Invoke(sessionId, guestId, "Book",
            $$"""{"table":"{{tableId}}","size":2}""");
        await Assert.That(book.Ok).IsTrue();
        var id = book.Result.ReturnInstanceId;
        await Assert.That(id).IsNotNull();

        var get = McpTestTools.Surface.Get(sessionId, id!);
        await Assert.That(get.Ok).IsTrue();
        var snapshot = SnapshotOf(get);

        await Assert.That(get.Property("Guest")).IsEqualTo($"#{guestId}");
        await Assert.That(get.Property("Table")).IsEqualTo($"#{tableId}");
        // Scalars and stage keep rendering as before.
        await Assert.That(get.Property("PartySize")).IsEqualTo("2");
        await Assert.That(snapshot["currentStage"]!.GetValue<string>()).IsEqualTo("Booked");
        await Assert.That(get.Data!.ToJsonString()).DoesNotContain("DomainEntityInstance");
    }

    [Test]
    public async Task StageLessEntity_CreateGetList_ReportsNoneStage() {
        var sessionId = McpTestTools.Surface.Open("InstanceStageLess");
        await Assert.That(McpTestTools.Surface.Define(sessionId,
            "domain InstanceStageLess Note: entity { Body: Text }").Success).IsTrue();

        var create = McpTestTools.Surface.Create(sessionId, "Note", """{"Body":"hi"}""");
        await Assert.That(create.Ok).IsTrue();
        await Assert.That(create.Summary).Contains("Stage: '(none)'");
        var createSnapshot = SnapshotOf(create);
        await Assert.That(createSnapshot["currentStage"]!.GetValue<string>()).IsEqualTo("(none)");

        var instanceId = createSnapshot["instanceId"]!.GetValue<string>();
        var get = McpTestTools.Surface.Get(sessionId, instanceId);
        await Assert.That(get.Ok).IsTrue();
        await Assert.That(get.Summary).Contains("stage: '(none)'");
        await Assert.That(SnapshotOf(get)["currentStage"]!.GetValue<string>()).IsEqualTo("(none)");

        var list = McpTestTools.Surface.List(sessionId);
        await Assert.That(list.Ok).IsTrue();
        var summary = ((JsonArray)list.Data!["instances"]!).Single()!;
        await Assert.That(summary["currentStage"]!.GetValue<string>()).IsEqualTo("(none)");
    }
}