using System.Text.Json;

using Poly.Mcp.Tools;
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
          Name: Text required
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
          Code: Text required
          Seats: Number required
          reservations: many Reservation
        }
        Reservation: entity {
          PartySize: Number required
          guest: Guest
          table: DiningTable
          Booked: stage { }
        }
        """;

    private static JsonElement SnapshotOf(DomainToolResponse response) {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(response.Data));
        return doc.RootElement.GetProperty("instance").Clone();
    }

    private static string? PropertyValue(JsonElement snapshot, string name) {
        foreach (var property in snapshot.GetProperty("properties").EnumerateArray()) {
            if (property.GetProperty("name").GetString() == name)
                return property.GetProperty("value").GetString();
        }
        return null;
    }

    private static string CreateAndId(string sessionId, string entity, string properties) {
        var create = McpTestTools.Runtime.CreateInstance(sessionId, entity, properties);
        if (!create.Success)
            throw new InvalidOperationException($"create_instance {entity}: {create.Message}");
        return SnapshotOf(create).GetProperty("instanceId").GetString()
            ?? throw new InvalidOperationException("missing instanceId");
    }

    [Test]
    public async Task GetInstance_NavigationProperty_RendersLinkedInstanceId() {
        var (sessionId, _) = McpTestTools.Store.Create("InstanceNavRender");
        await Assert.That(McpTestTools.Dsl.ApplyDsl(sessionId, NavDomain).Success).IsTrue();

        var guestId = CreateAndId(sessionId, "Guest", """{"Name":"Ada"}""");
        var tableId = CreateAndId(sessionId, "DiningTable", """{"Code":"T1","Seats":4}""");

        // Book binds the DiningTable action arg by instance id, links the table,
        // and back-references the Guest — both to-one navs land in the bag.
        var book = McpTestTools.Runtime.InvokeAction(sessionId, guestId, "Book",
            $$"""{"table":"{{tableId}}","size":2}""");
        await Assert.That(book.Success).IsTrue();
        using var bookDoc = JsonDocument.Parse(JsonSerializer.Serialize(book.Data));
        var id = bookDoc.RootElement.GetProperty("invokeActionResult")
            .GetProperty("returnInstanceId").GetString();
        await Assert.That(id).IsNotNull();

        var get = McpTestTools.Runtime.GetInstance(sessionId, id!);
        await Assert.That(get.Success).IsTrue();
        var snapshot = SnapshotOf(get);

        await Assert.That(PropertyValue(snapshot, "Guest")).IsEqualTo($"#{guestId}");
        await Assert.That(PropertyValue(snapshot, "Table")).IsEqualTo($"#{tableId}");
        // Scalars and stage keep rendering as before.
        await Assert.That(PropertyValue(snapshot, "PartySize")).IsEqualTo("2");
        await Assert.That(snapshot.GetProperty("currentStage").GetString()).IsEqualTo("Booked");
        await Assert.That(JsonSerializer.Serialize(get.Data)).DoesNotContain("DomainEntityInstance");
    }

    [Test]
    public async Task StageLessEntity_CreateGetList_ReportsNoneStage() {
        var (sessionId, _) = McpTestTools.Store.Create("InstanceStageLess");
        await Assert.That(McpTestTools.Dsl.ApplyDsl(sessionId,
            "domain InstanceStageLess Note: entity { Body: Text }").Success).IsTrue();

        var create = McpTestTools.Runtime.CreateInstance(sessionId, "Note", """{"Body":"hi"}""");
        await Assert.That(create.Success).IsTrue();
        await Assert.That(create.Message).Contains("Stage: '(none)'");
        var createSnapshot = SnapshotOf(create);
        await Assert.That(createSnapshot.GetProperty("currentStage").GetString()).IsEqualTo("(none)");

        var instanceId = createSnapshot.GetProperty("instanceId").GetString();
        var get = McpTestTools.Runtime.GetInstance(sessionId, instanceId!);
        await Assert.That(get.Success).IsTrue();
        await Assert.That(get.Message).Contains("stage: '(none)'");
        await Assert.That(SnapshotOf(get).GetProperty("currentStage").GetString()).IsEqualTo("(none)");

        var list = McpTestTools.Runtime.ListInstances(sessionId);
        await Assert.That(list.Success).IsTrue();
        using var listDoc = JsonDocument.Parse(JsonSerializer.Serialize(list.Data));
        var summary = listDoc.RootElement.GetProperty("instances").EnumerateArray().Single();
        await Assert.That(summary.GetProperty("currentStage").GetString()).IsEqualTo("(none)");
    }
}