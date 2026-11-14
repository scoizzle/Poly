using Poly.DomainModeling.Ontology;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.Mcp;

/// <summary>
/// L-15a (#683; owner ruling 2026-11-14, "Let create accept a link"): an MCP create takes a singular
/// navigation name whose value is the id of an existing instance, resolved the way an action's entity
/// argument is. A required to-one supplied this way satisfies Create's Required guard; an optional one may
/// be supplied too. A bad link value is a failure positioned on the navigation (<c>Entity.nav</c>) with the
/// kind a bad action argument gets (Constraint).
/// </summary>
public class CreateWithLinkTests {
    private const string Crm = """
        domain CrmLinks
        Account: entity {
          Name: Text
        }
        Contact: entity {
          Name: Text
        }
        Tag: entity {
          Label: Text
        }
        Lead: entity {
          Name: Text
          account: Account
          tags: many Tag
          HasAccount: policy { account exists }
        }
        """;

    private const string Hotel = """
        domain HotelLinks
        Property: entity {
          Name: Text
        }
        RoomType: entity {
          Code: Text
        }
        Room: entity {
          Number: Text
          property: Property
          roomType: RoomType optional
          HasType: policy { roomType exists }
        }
        """;

    private const string University = """
        domain UniversityLinks
        Course: entity {
          Code: Text
        }
        Instructor: entity {
          Name: Text
        }
        Section: entity {
          Cap: Number
          course: Course
          instructor: Instructor optional
          HasInstructor: policy { instructor exists }
          Open: stage {
            Close: action
              require HasInstructor
            {
              transition to Closed
            }
          }
          Closed: stage { }
        }
        """;

    private static (PolySurfaceHarness H, string Ws) Workspace(string dsl) {
        var h = PolySurfaceHarness.New();
        var ws = h.Open();
        var define = h.Define(ws, dsl);
        if (!define.Success)
            throw new InvalidOperationException($"define failed: {define.Summary}");
        return (h, ws);
    }

    private static string Id(HarnessStep step) => step.Ok
        ? step.InstanceId
        : throw new InvalidOperationException($"create failed: {step.Summary}");

    private static async Task AssertFailure(HarnessStep step, string kind, string elementPath, string message) {
        await Assert.That(step.Ok).IsFalse();
        await Assert.That(step.Summary).Contains(message);
        await Assert.That(step.Data?["failure"]?["kind"]?.GetValue<string>()).IsEqualTo(kind);
        await Assert.That(step.Data?["failure"]?["elementPath"]?.GetValue<string>()).IsEqualTo(elementPath);
        await Assert.That(step.Data?["failure"]?["message"]?.GetValue<string>()).IsEqualTo(message);
    }

    [Test]
    public async Task Create_CrmLeadWithItsAccount_SatisfiesTheRequiredLink() {
        var (h, ws) = Workspace(Crm);
        var account = Id(h.Create(ws, "Account", """{"Name":"Acme"}"""));

        var lead = h.Create(ws, "Lead", $$"""{"Name":"Grace","account":"{{account}}"}""");

        await Assert.That(lead.Ok).IsTrue().Because(lead.Summary);
        await Assert.That(h.Evaluate(ws, "Lead", "HasAccount", lead.InstanceId).Summary).Contains("true");
    }

    [Test]
    public async Task Create_WithoutARequiredLink_FailsRequiredOnTheNavigation() {
        var (h, ws) = Workspace(Crm);

        var lead = h.Create(ws, "Lead", """{"Name":"Grace"}""");

        await AssertFailure(lead, "Required", "Lead.account", "'account' is required.");
    }

    [Test]
    public async Task Create_HotelRoomWithARequiredAndAnOptionalLink_LinksBoth() {
        var (h, ws) = Workspace(Hotel);
        var property = Id(h.Create(ws, "Property", """{"Name":"Harbor"}"""));
        var type = Id(h.Create(ws, "RoomType", """{"Code":"KING"}"""));

        var room = h.Create(ws, "Room", $$"""{"Number":"101","property":"{{property}}","roomType":"{{type}}"}""");
        var bare = h.Create(ws, "Room", $$"""{"Number":"102","property":"{{property}}"}""");

        await Assert.That(room.Ok).IsTrue().Because(room.Summary);
        await Assert.That(h.Evaluate(ws, "Room", "HasType", room.InstanceId).Summary).Contains("true");
        await Assert.That(bare.Ok).IsTrue().Because(bare.Summary);
        await Assert.That(h.Evaluate(ws, "Room", "HasType", bare.InstanceId).Summary).Contains("false");
    }

    [Test]
    public async Task Create_UniversitySectionWithItsCourse_IsUnstaffedUntilItsInstructorIsLinked() {
        var (h, ws) = Workspace(University);
        var course = Id(h.Create(ws, "Course", """{"Code":"CS101"}"""));
        var instructor = Id(h.Create(ws, "Instructor", """{"Name":"Kim"}"""));

        var section = Id(h.Create(ws, "Section", $$"""{"Cap":10,"course":"{{course}}"}"""));
        var blocked = h.Invoke(ws, section, "Close");
        var linked = h.Link(ws, section, "instructor", instructor);
        var closed = h.Invoke(ws, section, "Close");

        await Assert.That(blocked.Result.Succeeded).IsFalse();
        await Assert.That(blocked.Result.FailedGuards).Contains("HasInstructor");
        await Assert.That(linked.Ok).IsTrue().Because(linked.Summary);
        await Assert.That(closed.Result.Succeeded).IsTrue().Because(closed.Summary);
        await Assert.That(closed.Result.NewStage).IsEqualTo("Closed");
    }

    [Test]
    public async Task Create_LinkToAnUnknownId_IsAPositionedFailure() {
        var (h, ws) = Workspace(Crm);

        var lead = h.Create(ws, "Lead", """{"Name":"Grace","account":"nope"}""");

        await AssertFailure(lead, "Constraint", "Lead.account", "Navigation 'account': instance 'nope' of 'Account' not found.");
    }

    [Test]
    public async Task Create_LinkToAnInstanceOfTheWrongEntity_IsAPositionedFailure() {
        var (h, ws) = Workspace(Crm);
        var contact = Id(h.Create(ws, "Contact", """{"Name":"Ada"}"""));

        var lead = h.Create(ws, "Lead", $$"""{"Name":"Grace","account":"{{contact}}"}""");

        await AssertFailure(lead, "Constraint", "Lead.account",
            $"Navigation 'account': instance '{contact}' is a 'Contact', expected 'Account'.");
    }

    [Test]
    public async Task Create_ACollectionNavigation_IsAPositionedFailure() {
        var (h, ws) = Workspace(Crm);
        var account = Id(h.Create(ws, "Account", """{"Name":"Acme"}"""));
        var tag = Id(h.Create(ws, "Tag", """{"Label":"hot"}"""));

        var lead = h.Create(ws, "Lead", $$"""{"Name":"Grace","account":"{{account}}","tags":"{{tag}}"}""");

        await AssertFailure(lead, "Constraint", "Lead.tags",
            "Navigation 'tags' is a collection: create takes a singular navigation's instance id; link each item after create with link.");
    }

    [Test]
    public async Task Create_ANavigationGivenANonIdValue_IsAPositionedFailure() {
        var (h, ws) = Workspace(Crm);

        var lead = h.Create(ws, "Lead", """{"Name":"Grace","account":42}""");

        await AssertFailure(lead, "Constraint", "Lead.account",
            "Navigation 'account' takes the id of an existing 'Account' instance; got 42.");
    }

    [Test]
    public async Task Simulate_CreateStepWithALinkToAnEarlierAlias_SatisfiesTheRequiredLink() {
        var (h, ws) = Workspace(Crm);

        var run = h.Simulate(ws,
            SimStep.Create("Account", """{"Name":"Acme"}""", alias: "acme"),
            SimStep.Create("Lead", """{"Name":"Grace","account":"acme"}""", alias: "grace"));

        await Assert.That(run.Step(1).Ok).IsTrue().Because(run.Step(1).Summary);
        await Assert.That(h.Evaluate(ws, "Lead", "HasAccount", run.Step(1).InstanceId).Summary).Contains("true");
    }

    [Test]
    public async Task Unlink_ARequiredToOne_IsRefused() {
        var (h, ws) = Workspace(Crm);
        var account = Id(h.Create(ws, "Account", """{"Name":"Acme"}"""));
        var lead = Id(h.Create(ws, "Lead", $$"""{"Name":"Grace","account":"{{account}}"}"""));

        var unlink = h.Unlink(ws, lead, "account", account);

        await Assert.That(unlink.Ok).IsFalse();
        await Assert.That(unlink.Summary).Contains(Relationship.RequiredUnlinkViolationMessage("account"));
        await Assert.That(h.Evaluate(ws, "Lead", "HasAccount", lead).Summary).Contains("true");
    }
}