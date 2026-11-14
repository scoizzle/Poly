using System.Reflection;

using Poly.Ast.Nodes;
using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Libraries.Storage;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;
using Poly.Mcp.Tools;
using Poly.Runtime;

namespace Poly.Tests.DomainModeling.Runtime;

/// <summary>
/// S13.2a (#368): the host seam that runs the lowered module on dictionary
/// instances. Nothing here constructs an instance wrapper or an instance store.
/// </summary>
public sealed class ModuleInstanceExecutorTests {
    [Test]
    public async Task Create_RunsCachedFactory_ReturnsBagAndEntityName() {
        var executor = ExecutorFor("""
            domain Ledger
            Entry: entity { Memo: Text }
            """);

        var created = executor.Create("Entry", Values(("Memo", "first")));

        await Assert.That(created.Succeeded).IsTrue();
        var entry = created.Instance!;
        await Assert.That(entry.EntityName).IsEqualTo("Entry");
        await Assert.That(entry.Bag["Memo"]).IsEqualTo("first");
        await Assert.That(executor.TryGet(entry.Id, out var found)).IsTrue();
        await Assert.That(ReferenceEquals(found, entry)).IsTrue();
    }

    // ── create / storage port ────────────────────────────────────────────────

    private const string Unique = """
        domain Lab
        Widget: entity {
          Code: Text unique
          Hits: Number default(0)
          Relabel: action (code: Text) {
            assign Code to code
          }
        }
        """;

    [Test]
    public async Task Create_DuplicateUnique_FailsThroughSessionPortAndRegistersNothing() {
        var executor = ExecutorFor(Unique);
        var first = executor.Create("Widget", Values(("Code", "A")));
        var second = executor.Create("Widget", Values(("Code", "A")));

        await Assert.That(first.Succeeded).IsTrue();
        await Assert.That(second.Succeeded).IsFalse();
        await Assert.That(second.Error).Contains("Unique constraint violated");
        await Assert.That(executor.Instances.Count).IsEqualTo(1);
        await Assert.That(executor.Storage.Exists("Widget", "Code", "A")).IsTrue();
    }

    [Test]
    public async Task Storage_IsScopedToTheExecutor() {
        var domain = Lower(Unique);
        var one = new ModuleInstanceExecutor(domain);
        var other = new ModuleInstanceExecutor(domain);

        one.Create("Widget", Values(("Code", "A")));
        var elsewhere = other.Create("Widget", Values(("Code", "A")));

        await Assert.That(elsewhere.Succeeded).IsTrue();
        await Assert.That(ReferenceEquals(one.Storage, other.Storage)).IsFalse();
    }

    [Test]
    public async Task Invoke_WithStoragePortArgument_AssignsWhenFree() {
        var executor = ExecutorFor(Unique);
        var widget = executor.Create("Widget", Values(("Code", "A"))).Instance!;

        var outcome = executor.Invoke(widget, "Relabel", Values(("code", "B")));

        await Assert.That(outcome.Succeeded).IsTrue();
        await Assert.That(widget.Bag["Code"]).IsEqualTo("B");
        await Assert.That(executor.Storage.Exists("Widget", "Code", "B")).IsTrue();
        await Assert.That(executor.Storage.Exists("Widget", "Code", "A")).IsFalse();
    }

    [Test]
    public async Task Invoke_UniqueCollision_FailsAndLeavesBagAndPortUnchanged() {
        var executor = ExecutorFor(Unique);
        var a = executor.Create("Widget", Values(("Code", "A"))).Instance!;
        executor.Create("Widget", Values(("Code", "B")));

        var outcome = executor.Invoke(a, "Relabel", Values(("code", "B")));

        await Assert.That(outcome.Succeeded).IsFalse();
        await Assert.That(outcome.Error).Contains("Unique constraint violated");
        await Assert.That(a.Bag["Code"]).IsEqualTo("A");
        await Assert.That(executor.Storage.Exists("Widget", "Code", "A")).IsTrue();
    }

    // ── actions: unknown / missing / coercion ────────────────────────────────

    [Test]
    public async Task Invoke_UnknownAction_AndBadArguments_FailClosed() {
        var executor = ExecutorFor(Unique);
        var widget = executor.Create("Widget", Values(("Code", "A"))).Instance!;

        var unknown = executor.Invoke(widget, "Nope");
        var missing = executor.Invoke(widget, "Relabel");
        var extra = executor.Invoke(widget, "Relabel", Values(("code", "B"), ("bogus", 1L)));
        var internalMethod = executor.Invoke(widget, "EnsureUnique");

        await Assert.That(unknown.Succeeded).IsFalse();
        await Assert.That(unknown.Error).Contains("not found");
        await Assert.That(missing.Error).Contains("Missing argument 'code'");
        await Assert.That(extra.Error).Contains("Unknown argument 'bogus'");
        await Assert.That(internalMethod.Succeeded).IsFalse();
        await Assert.That(widget.Bag["Code"]).IsEqualTo("A");
    }

    private const string Typed = """
        domain Typed
        Booking: entity {
          Title: Text
          Seats: Number
          Fee: Decimal
          Confirmed: Boolean
          Token: Uuid
          Reschedule: action (token: Uuid, seats: Number) {
            assign Token to token
            assign Seats to seats
          }
        }
        """;

    [Test]
    public async Task Create_CoercesJsonIshValues() {
        var executor = ExecutorFor(Typed);

        var outcome = executor.Create("Booking", Values(
            ("title", "Opening"), ("seats", 12m), ("fee", 40L),
            ("confirmed", 1L), ("token", "6f9619ff-8b86-d011-b42d-00c04fc964ff")));

        await Assert.That(outcome.Succeeded).IsTrue();
        var bag = outcome.Instance!.Bag;
        await Assert.That(bag["Seats"]).IsEqualTo(12L);
        await Assert.That(bag["Fee"]).IsEqualTo(40m);
        await Assert.That(bag["Confirmed"] is true).IsTrue();
        await Assert.That(bag["Token"]).IsEqualTo(Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff"));
    }

    [Test]
    public async Task Create_NonIntegralNumber_AndUnknownProperty_FailWithoutRegistering() {
        var executor = ExecutorFor(Typed);

        var fractional = executor.Create("Booking", Values(
            ("title", "x"), ("seats", 1.5m), ("fee", 1m), ("confirmed", true), ("token", "6f9619ff-8b86-d011-b42d-00c04fc964ff")));
        var unknown = executor.Create("Booking", Values(("nope", 1L)));

        await Assert.That(fractional.Succeeded).IsFalse();
        await Assert.That(fractional.Error).Contains("Number is integral");
        await Assert.That(unknown.Succeeded).IsFalse();
        await Assert.That(unknown.Error).Contains("Unknown property 'nope'");
        await Assert.That(executor.Instances.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Invoke_CoercesActionArguments() {
        var executor = ExecutorFor(Typed);
        var booking = executor.Create("Booking", Values(
            ("title", "x"), ("seats", 1L), ("fee", 1m), ("confirmed", false), ("token", "6f9619ff-8b86-d011-b42d-00c04fc964ff"))).Instance!;

        var outcome = executor.Invoke(booking, "Reschedule", Values(("token", "11111111-2222-3333-4444-555555555555"), ("seats", 3m)));

        await Assert.That(outcome.Succeeded).IsTrue();
        await Assert.That(booking.Bag["Token"]).IsEqualTo(Guid.Parse("11111111-2222-3333-4444-555555555555"));
        await Assert.That(booking.Bag["Seats"]).IsEqualTo(3L);

        var bad = executor.Invoke(booking, "Reschedule", Values(("token", "not a guid"), ("seats", 3L)));
        await Assert.That(bad.Succeeded).IsFalse();
        await Assert.That(bad.Error).Contains("expects Guid");
    }

    // ── created children, snapshot / rollback ────────────────────────────────

    private const string Parking = """
        domain Parking
        Permit: entity { Plate: Text unique }
        Lot: entity {
          Issued: Number default(0)
          permits: many Permit
          Issue: action {
            create in permits { Plate: "P1" }
            assign Issued to Issued + 1
          }
          IssueTwice: action {
            create in permits { Plate: "P2" }
            assign Issued to Issued + 1
            create in permits { Plate: "P2" }
          }
        }
        """;

    [Test]
    public async Task Invoke_CreateIn_DetectsAndRegistersCreatedChildren() {
        var executor = ExecutorFor(Parking);
        var lot = executor.Create("Lot", Values()).Instance!;

        var outcome = executor.Invoke(lot, "Issue");

        await Assert.That(outcome.Succeeded).IsTrue();
        await Assert.That(outcome.CreatedChildren.Count).IsEqualTo(1);
        var permit = outcome.CreatedChildren[0];
        await Assert.That(permit.EntityName).IsEqualTo("Permit");
        await Assert.That(permit.Bag["Plate"]).IsEqualTo("P1");
        await Assert.That(executor.TryGet(permit.Id, out var found)).IsTrue();
        await Assert.That(ReferenceEquals(found, permit)).IsTrue();
        await Assert.That(executor.IsLinked("permits", lot, permit)).IsTrue();
        await Assert.That(executor.LinkedTargets("permits", lot).Count).IsEqualTo(1);
        await Assert.That(executor.LinkedSources("permits", permit).Single().Id).IsEqualTo(lot.Id);
        await Assert.That(lot.Bag["Issued"]).IsEqualTo(1L);

        // The registered child is not reported again: the second run's own P1 collides.
        var again = executor.Invoke(lot, "Issue");
        await Assert.That(again.Succeeded).IsFalse();
        await Assert.That(executor.Instances.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Invoke_DiscardedBareCreate_IsStillAdoptedThroughBorn() {
        // Follow-up 3 (R-03a): a bare `create` that is neither linked nor returned reaches the
        // executor only through the heap's born instances, so it must still be registered.
        var executor = ExecutorFor("""
            domain Desk
            Ticket: entity { Subject: Text }
            Desk: entity {
              tickets: many Ticket
              Note: action {
                create Ticket { Subject: "loose" }
              }
            }
            """);
        var desk = executor.Create("Desk", Values()).Instance!;

        var outcome = executor.Invoke(desk, "Note");

        await Assert.That(outcome.Succeeded).IsTrue();
        await Assert.That(executor.Instances.Count).IsEqualTo(2);
        var ticket = executor.Instances.Single(i => i.EntityName == "Ticket");
        await Assert.That(ticket.Bag["Subject"]).IsEqualTo("loose");
        await Assert.That(executor.LinkedTargets("tickets", desk).Count).IsEqualTo(0);
    }

    [Test]
    public async Task Invoke_ActionReturningEntity_ReturnsTheRegisteredChild() {
        var executor = ExecutorFor("""
            domain Desk
            Ticket: entity { Subject: Text }
            Desk: entity {
              tickets: many Ticket
              Open: action (subject: Text) -> Ticket {
                create in tickets { Subject: subject }
              }
            }
            """);
        var desk = executor.Create("Desk", Values()).Instance!;

        var outcome = executor.Invoke(desk, "Open", Values(("subject", "Printer")));

        await Assert.That(outcome.Succeeded).IsTrue();
        await Assert.That(outcome.CreatedChildren.Count).IsEqualTo(1);
        await Assert.That(outcome.Instance).IsNotNull();
        await Assert.That(ReferenceEquals(outcome.Instance, outcome.CreatedChildren[0])).IsTrue();
        await Assert.That(outcome.Instance!.Bag["Subject"]).IsEqualTo("Printer");
    }

    [Test]
    public async Task Invoke_FailureMidAction_RollsBackAssignsChildrenAndPort() {
        var executor = ExecutorFor(Parking);
        var lot = executor.Create("Lot", Values()).Instance!;

        var outcome = executor.Invoke(lot, "IssueTwice");

        await Assert.That(outcome.Succeeded).IsFalse();
        await Assert.That(outcome.Error).Contains("Unique constraint violated");
        await Assert.That(lot.Bag["Issued"]).IsEqualTo(0L);
        await Assert.That(((System.Collections.IList)lot.Bag["_permits"]!).Count).IsEqualTo(0);
        await Assert.That(executor.Instances.Count).IsEqualTo(1);
        await Assert.That(executor.Storage.Exists("Permit", "Plate", "P2")).IsFalse();
    }

    // ── policies ─────────────────────────────────────────────────────────────

    private const string Orders = """
        domain Shop
        Order: entity { Total: Number }
        Customer: entity {
          Name: Text
          orders: many Order
          IsNamed: policy { Name is "Ada" }
          HasBigOrder: policy { any orders where Total > 100 }
        }
        """;

    [Test]
    public async Task EvaluatePolicy_ReadsPropertiesAndLinksFromTheBag() {
        var executor = ExecutorFor(Orders);
        var ada = executor.Create("Customer", Values(("Name", "Ada"))).Instance!;
        var bob = executor.Create("Customer", Values(("Name", "Bob"))).Instance!;
        var small = executor.Create("Order", Values(("Total", 5L))).Instance!;
        var big = executor.Create("Order", Values(("Total", 500L))).Instance!;

        await Assert.That(executor.EvaluatePolicy(ada, "IsNamed")).IsTrue();
        await Assert.That(executor.EvaluatePolicy(bob, "IsNamed")).IsFalse();
        await Assert.That(executor.EvaluatePolicy(ada, "HasBigOrder")).IsFalse();

        executor.Link("orders", ada, small);
        await Assert.That(executor.EvaluatePolicy(ada, "HasBigOrder")).IsFalse();
        executor.Link("orders", ada, big);
        await Assert.That(executor.EvaluatePolicy(ada, "HasBigOrder")).IsTrue();
        executor.Unlink("orders", ada, big);
        await Assert.That(executor.EvaluatePolicy(ada, "HasBigOrder")).IsFalse();
    }

    [Test]
    public async Task EvaluatePolicy_UnknownPolicy_FailsClosed() {
        var executor = ExecutorFor(Orders);
        var ada = executor.Create("Customer", Values(("Name", "Ada"))).Instance!;

        await Assert.That(() => executor.EvaluatePolicy(ada, "Missing"))
            .Throws<InvalidOperationException>();
    }

    // ── link / unlink ────────────────────────────────────────────────────────

    [Test]
    public async Task Link_Unlink_ToMany_IsIdempotentAndWritesTheCollectionField() {
        var executor = ExecutorFor(Orders);
        var ada = executor.Create("Customer", Values(("Name", "Ada"))).Instance!;
        var order = executor.Create("Order", Values(("Total", 5L))).Instance!;

        await Assert.That(executor.Link("orders", ada, order).Succeeded).IsTrue();
        await Assert.That(executor.Link("orders", ada, order).Succeeded).IsTrue();
        await Assert.That(((System.Collections.IList)ada.Bag["_orders"]!).Count).IsEqualTo(1);
        await Assert.That(executor.IsLinked("orders", ada, order)).IsTrue();

        await Assert.That(executor.Unlink("orders", ada, order).Succeeded).IsTrue();
        await Assert.That(executor.Unlink("orders", ada, order).Succeeded).IsTrue();
        await Assert.That(((System.Collections.IList)ada.Bag["_orders"]!).Count).IsEqualTo(0);
    }

    [Test]
    public async Task Link_AlreadyLinkedPair_ReportsAlreadyLinked() {
        var executor = ExecutorFor(Orders);
        var ada = executor.Create("Customer", Values(("Name", "Ada"))).Instance!;
        var order = executor.Create("Order", Values(("Total", 5L))).Instance!;

        var first = executor.Link("orders", ada, order);
        var second = executor.Link("orders", ada, order);

        await Assert.That(first.Succeeded).IsTrue();
        await Assert.That(first.Change).IsEqualTo(LinkChange.Linked);
        await Assert.That(second.Succeeded).IsTrue();
        await Assert.That(second.Change).IsEqualTo(LinkChange.AlreadyLinked);
        await Assert.That(((System.Collections.IList)ada.Bag["_orders"]!).Count).IsEqualTo(1);

        var unlinked = executor.Unlink("orders", ada, order);
        var alreadyUnlinked = executor.Unlink("orders", ada, order);

        await Assert.That(unlinked.Succeeded).IsTrue();
        await Assert.That(unlinked.Change).IsEqualTo(LinkChange.Unlinked);
        await Assert.That(alreadyUnlinked.Succeeded).IsTrue();
        await Assert.That(alreadyUnlinked.Change).IsEqualTo(LinkChange.NotLinked);
        await Assert.That(((System.Collections.IList)ada.Bag["_orders"]!).Count).IsEqualTo(0);
    }

    [Test]
    public async Task Link_UnknownRelationship_AndWrongTarget_Fail() {
        var executor = ExecutorFor(Orders);
        var ada = executor.Create("Customer", Values(("Name", "Ada"))).Instance!;
        var bob = executor.Create("Customer", Values(("Name", "Bob"))).Instance!;

        var unknown = executor.Link("nope", ada, bob);
        var wrong = executor.Link("orders", ada, bob);

        await Assert.That(unknown.Succeeded).IsFalse();
        await Assert.That(unknown.Error).Contains("not found");
        await Assert.That(wrong.Succeeded).IsFalse();
        await Assert.That(wrong.Error).Contains("not 'Customer'");
    }

    private const string Passports = """
        domain Travel
        Profile: entity { Bio: Text }
        Passport: entity { Number: Text }
        Person: entity {
          Name: Text
          profile: one Profile
          passport: owned Passport
        }
        """;

    [Test]
    public async Task Link_SecondLinkOnToOneSource_IsRefused() {
        var executor = ExecutorFor(Passports);
        var person = executor.Create("Person", Values(("Name", "Ada"))).Instance!;
        var first = executor.Create("Profile", Values(("Bio", "a"))).Instance!;
        var second = executor.Create("Profile", Values(("Bio", "b"))).Instance!;

        await Assert.That(executor.Link("profile", person, first).Succeeded).IsTrue();
        var refused = executor.Link("profile", person, second);

        await Assert.That(refused.Succeeded).IsFalse();
        await Assert.That(refused.Error).IsEqualTo(Relationship.LinkViolationMessage("profile"));
        await Assert.That(ReferenceEquals(person.Bag["Profile"], first.Bag)).IsTrue();

        await Assert.That(executor.Unlink("profile", person, first).Succeeded).IsTrue();
        await Assert.That(person.Bag["Profile"]).IsNull();
        await Assert.That(executor.Link("profile", person, second).Succeeded).IsTrue();
    }

    [Test]
    public async Task Unlink_LastOwnerOfOwnedTarget_IsRefused_ButASecondOwnerMayLeave() {
        var executor = ExecutorFor(Passports);
        var ada = executor.Create("Person", Values(("Name", "Ada"))).Instance!;
        var bob = executor.Create("Person", Values(("Name", "Bob"))).Instance!;
        var passport = executor.Create("Passport", Values(("Number", "N1"))).Instance!;

        executor.Link("passport", ada, passport);
        var refused = executor.Unlink("passport", ada, passport);
        await Assert.That(refused.Succeeded).IsFalse();
        await Assert.That(refused.Error).IsEqualTo(Relationship.UnlinkViolationMessage("passport"));
        await Assert.That(executor.IsLinked("passport", ada, passport)).IsTrue();

        executor.Link("passport", bob, passport);
        await Assert.That(executor.Unlink("passport", ada, passport).Succeeded).IsTrue();
        await Assert.That(executor.Unlink("passport", bob, passport).Succeeded).IsFalse();
    }

    // ── subscriber registry ──────────────────────────────────────────────────

    private const string Watch = """
        domain Watch
        Item: entity {
          Code: Text
          watcher: Watcher via items
          Draft: stage {
            Activate: action { transition to Active }
          }
          Active: stage { }
        }
        Watcher: entity {
          Hits: Number default(0)
          items: many Item
          when any items Active {
            assign Hits to Hits + 1
          }
        }
        """;

    [Test]
    public async Task Link_RegistersSubscriber_SoTheModuleNotifyBodyFansOut_UnlinkRemovesIt() {
        var executor = ExecutorFor(Watch);
        var watcher = executor.Create("Watcher", Values()).Instance!;
        var item = executor.Create("Item", Values(("Code", "I1"))).Instance!;
        var other = executor.Create("Item", Values(("Code", "I2"))).Instance!;

        executor.Link("items", watcher, item);
        executor.Link("items", watcher, other);
        executor.Unlink("items", watcher, other);

        await Assert.That(executor.Invoke(item, "Activate").Succeeded).IsTrue();
        await Assert.That(watcher.Bag["Hits"]).IsEqualTo(1L);

        await Assert.That(executor.Invoke(other, "Activate").Succeeded).IsTrue();
        await Assert.That(watcher.Bag["Hits"]).IsEqualTo(1L);
        await Assert.That(other.Bag["_watcherItemsActiveSubscribers"]).IsNull();
    }

    // ── behaviours ported off the retired DEI suites (S13.4b) ──────────────

    [Test]
    public async Task Invoke_FailingRequire_ReturnsTheGuardNameInFailedGuards() {
        var executor = ExecutorFor("""
            domain Shop
            Person: entity {
              Age: Number
              IsAdult: policy { Age >= 18 }
              Draft: stage { }
              Active: stage { }
              Activate: action require IsAdult { transition to Active }
            }
            """);
        var minor = executor.Create("Person", Values(("Age", 10L))).Instance!;

        var blocked = executor.Invoke(minor, "Activate");

        await Assert.That(blocked.Succeeded).IsFalse();
        await Assert.That(blocked.FailedGuards).Contains("IsAdult");
        await Assert.That(blocked.Error).Contains("blocked by policy 'IsAdult'");
        await Assert.That(Stage(minor)).IsEqualTo("Draft");
    }

    // ── typed outcome failure (X-01a) ────────────────────────────────────────

    private const string PolicyBlockDsl = """
        domain Shop
        Person: entity {
          Age: Number
          IsAdult: policy { Age >= 18 }
          Draft: stage { }
          Active: stage { }
          Activate: action require IsAdult { transition to Active }
        }
        """;

    private const string SectionDsl = """
        domain EnrollSlice
        Section: entity {
          SeatsTaken: Number default(0)
        }
        Enrollment: entity {
          section: Section
          SectionFull: policy { section SeatsTaken >= 1 }
          Pending: stage {
            Confirm: action require not SectionFull {
              transition to Confirmed
            }
          }
          Confirmed: stage { }
        }
        """;

    [Test]
    public async Task Invoke_PolicyBlocked_OutcomeFailureHasGuardKindAndElement() {
        var executor = ExecutorFor(PolicyBlockDsl);
        var minor = executor.Create("Person", Values(("Age", 10L))).Instance!;

        var blocked = executor.Invoke(minor, "Activate");

        await Assert.That(blocked.Succeeded).IsFalse();
        await Assert.That(blocked.Failure!.Kind).IsEqualTo(DomainFailureKind.Policy);
        await Assert.That(blocked.Failure.Guard).IsEqualTo("IsAdult");
        await Assert.That(blocked.Failure.ElementPath).IsEqualTo("Person.Activate");
        await Assert.That(blocked.Failure.Message).Contains("blocked by policy 'IsAdult'");
        await Assert.That(Stage(minor)).IsEqualTo("Draft");
    }

    [Test]
    public async Task Create_DuplicateUnique_OutcomeFailureHasUniqueKindAndElementPath() {
        var executor = ExecutorFor(Unique);
        executor.Create("Widget", Values(("Code", "A")));
        var second = executor.Create("Widget", Values(("Code", "A")));

        await Assert.That(second.Succeeded).IsFalse();
        await Assert.That(second.Failure!.Kind).IsEqualTo(DomainFailureKind.Unique);
        await Assert.That(second.Failure.ElementPath).IsEqualTo("Widget.Code");
        await Assert.That(second.Failure.Message).Contains("Unique constraint violated");
        await Assert.That(executor.Instances.Count).IsEqualTo(1);
        await Assert.That(executor.Storage.Exists("Widget", "Code", "A")).IsTrue();
    }

    [Test]
    public async Task Invoke_PathPrefixRequire_GuardListKeepsEveryActionPolicy() {
        var executor = ExecutorFor(SectionDsl);
        var enrollment = executor.Create("Enrollment", Values()).Instance!;

        var blocked = executor.Invoke(enrollment, "Confirm");

        await Assert.That(blocked.Failure!.Kind).IsEqualTo(DomainFailureKind.Required);
        await Assert.That(blocked.Failure.ElementPath).IsEqualTo("Enrollment.section");
        await Assert.That(blocked.Failure.Guard).IsNull();
        await Assert.That(blocked.FailedGuards).Contains("not_SectionFull");
    }

    [Test]
    public async Task Invoke_RequireNot_PolicyBlocked_FailureGuardIsTheRealPolicyName() {
        var executor = ExecutorFor(SectionDsl);
        var section = executor.Create("Section", Values()).Instance!;
        var enrollment = executor.Create("Enrollment", Values()).Instance!;
        executor.Link("section", enrollment, section);
        section.Bag["SeatsTaken"] = 1L;

        var blocked = executor.Invoke(enrollment, "Confirm");

        await Assert.That(blocked.Failure!.Kind).IsEqualTo(DomainFailureKind.Policy);
        await Assert.That(blocked.Failure.Guard).IsEqualTo("SectionFull");
        await Assert.That(string.Join(",", blocked.FailedGuards)).IsEqualTo("SectionFull");
    }

    [Test]
    public async Task Invoke_StageGateFailure_FailedGuardsIsEmpty() {
        var executor = ExecutorFor("""
            domain Gate
            Item: entity {
              Draft: stage { Go: action { transition to Done } }
              Done: stage { }
            }
            """);
        var item = executor.Create("Item", Values()).Instance!;
        await Assert.That(executor.Invoke(item, "Go").Succeeded).IsTrue();

        var again = executor.Invoke(item, "Go");

        await Assert.That(again.Succeeded).IsFalse();
        await Assert.That(again.Failure!.Kind).IsEqualTo(DomainFailureKind.Stage);
        await Assert.That(again.Failure.Guard).IsNotNull();
        await Assert.That(again.FailedGuards.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Invoke_MultiHopPathPrefix_SecondHopUnlinked_ListsActionPolicies() {
        var executor = ExecutorFor("""
            domain IssueSlice
            Engineer: entity { team: Team }
            Team: entity { TeamName: Text }
            Issue: entity {
              reporter: Engineer
              FromBlueTeam: policy { reporter team TeamName is "Blue" }
              Open: stage {
                Escalate: action require FromBlueTeam { transition to Closed }
              }
              Closed: stage { }
            }
            """);
        var engineer = executor.Create("Engineer", Values()).Instance!;
        var issue = executor.Create("Issue", Values()).Instance!;
        executor.Link("reporter", issue, engineer);

        var blocked = executor.Invoke(issue, "Escalate");

        await Assert.That(blocked.Failure!.Kind).IsEqualTo(DomainFailureKind.Required);
        await Assert.That(blocked.Failure.ElementPath).IsEqualTo("Engineer.team");
        await Assert.That(blocked.FailedGuards).Contains("FromBlueTeam");
    }

    [Test]
    public async Task ExecutorSource_ContainsNoBlockedByPolicyLiteral() {
        var directory = Path.Combine(GoldenDomains.RepoRoot, "Poly", "DomainModeling", "Runtime");
        var files = Directory.GetFiles(directory, "ModuleInstanceExecutor*.cs");

        await Assert.That(files.Length).IsGreaterThan(0);
        foreach (var file in files) {
            var text = await File.ReadAllTextAsync(file);
            await Assert.That(text).DoesNotContain("blocked by policy '");
            await Assert.That(text).DoesNotContain("requires a linked");
        }
    }

    private const string InvokeUnlinkedGuardedDsl = """
        domain Lab
        Child: entity { Poke: action { } }
        Parent: entity {
          Armed: Boolean default(true)
          Ready: policy { Armed is true }
          kid: Child
          Go: action require Ready { invoke kid.Poke }
        }
        """;

    [Test]
    public async Task Invoke_InvokeUnlinked_FailedGuardsListsTheActionPolicies() {
        var executor = ExecutorFor(InvokeUnlinkedGuardedDsl);
        var parent = executor.Create("Parent", Values()).Instance!;

        var blocked = executor.Invoke(parent, "Go");

        await Assert.That(blocked.Failure!.Kind).IsEqualTo(DomainFailureKind.Required);
        await Assert.That(blocked.Failure.ElementPath).IsEqualTo("Parent.kid");
        await Assert.That(blocked.Failure.Guard).IsNull();
        await Assert.That(string.Join(",", blocked.FailedGuards)).IsEqualTo("Ready");
    }

    private const string RewrappedRequiredDsl = """
        domain Lab
        Owner: entity { Flag: Boolean default(true) }
        Child: entity {
          owner: Owner
          OwnerFlagged: policy { owner Flag is true }
          Poke: action require OwnerFlagged { }
        }
        Parent: entity {
          Armed: Boolean default(true)
          Ready: policy { Armed is true }
          kid: Child
          AddKid: action { create in kid { } }
          Go: action require Ready { invoke kid.Poke }
        }
        """;

    /// <summary>
    /// X-01b ruling (a): a callee's path-prefix <c>Required</c>, rewrapped through <c>At</c> into a path
    /// of more than two segments, projects <c>[]</c>. The caller's policies did not fail; the old text
    /// parser listed them only because the callee's message contained the linked-target phrase.
    /// The MCP mapper keeps listing the caller's policies until M-09 (see the cross-parser pin).
    /// </summary>
    [Test]
    public async Task Invoke_RewrappedCalleeRequired_ProjectsNoGuards() {
        var executor = ExecutorFor(RewrappedRequiredDsl);
        var parent = executor.Create("Parent", Values()).Instance!;
        await Assert.That(executor.Invoke(parent, "AddKid").Succeeded).IsTrue();

        var blocked = executor.Invoke(parent, "Go");

        await Assert.That(blocked.Succeeded).IsFalse();
        await Assert.That(blocked.Failure!.Kind).IsEqualTo(DomainFailureKind.Required);
        await Assert.That(blocked.Failure.ElementPath).IsEqualTo("Parent.Go.Child.owner");
        await Assert.That(blocked.Failure.Guard).IsNull();
        await Assert.That(blocked.FailedGuards.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Invoke_PolicyFailureWithoutGuard_ProjectsNoGuards() {
        var domain = Lower(PolicyBlockDsl);
        var executor = new ModuleInstanceExecutor(domain);
        var person = executor.Create("Person", Values(("Age", 30L))).Instance!;
        ReplaceBody(domain, "Person", "Activate",
            new Constant(new FixedResult(DomainFailure.Policy("blocked", "Person.Activate"))));

        var blocked = executor.Invoke(person, "Activate");

        await Assert.That(blocked.Failure!.Kind).IsEqualTo(DomainFailureKind.Policy);
        await Assert.That(blocked.Failure.Guard).IsNull();
        await Assert.That(blocked.FailedGuards.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Invoke_RequiredOutsideANavigationPath_ProjectsNoGuards() {
        var domain = Lower(PolicyBlockDsl);
        var executor = new ModuleInstanceExecutor(domain);
        var person = executor.Create("Person", Values(("Age", 30L))).Instance!;
        ReplaceBody(domain, "Person", "Activate",
            new Constant(new FixedResult(DomainFailure.Required("'Age' is required.", "Person.Age"))));

        var blocked = executor.Invoke(person, "Activate");

        await Assert.That(blocked.Failure!.Kind).IsEqualTo(DomainFailureKind.Required);
        await Assert.That(blocked.Failure.ElementPath).IsEqualTo("Person.Age");
        await Assert.That(blocked.FailedGuards.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Invoke_FailedResultWithoutError_ThrowsRatherThanFabricatingAFailure() {
        var domain = Lower(PolicyBlockDsl);
        var executor = new ModuleInstanceExecutor(domain);
        var person = executor.Create("Person", Values(("Age", 30L))).Instance!;
        ReplaceBody(domain, "Person", "Activate", new Constant(new FixedResult(null)));

        var thrown = Assert.Throws<InvalidOperationException>(() => executor.Invoke(person, "Activate"));

        await Assert.That(thrown.Message).Contains("failed without a DomainFailure");
    }

    [Test]
    public async Task Create_FailedResultWithoutError_ThrowsRatherThanFabricatingAFailure() {
        var domain = Lower(PolicyBlockDsl);
        var executor = new ModuleInstanceExecutor(domain);
        ReplaceBody(domain, "Person", "Create", new Constant(new FixedResult(null)));

        var thrown = Assert.Throws<InvalidOperationException>(() => executor.Create("Person", Values()));

        await Assert.That(thrown.Message).Contains("failed without a DomainFailure");
    }

    /// <summary>A hand-written result, so a test can return a state the lowered module never produces.</summary>
    private sealed record FixedResult(DomainFailure? Error) : IDomainResult {
        public bool IsSuccess => false;
        public string? ErrorMessage => Error?.Render();
    }

    /// <summary>Swaps one module method's body in the domain's cached module, the seam a hand-written result needs.</summary>
    private static void ReplaceBody(Domain domain, string entity, string method, Node body) {
        RuntimeAnalysisCache.TryGetModule(domain, out var module);
        RuntimeAnalysisCache.ReplaceModule(domain, [.. module.Select(t => t.Name != entity ? t : t with {
            Methods = [.. t.Methods!.Select(m => m.Name == method ? m with { Body = body } : m)],
        })]);
    }

    /// <summary>
    /// Until M-09 the executor's typed <c>GuardsFor</c> and the MCP text mapper implement one rule twice.
    /// This pins where they agree (an own policy block, a path-prefix require) and the one named split:
    /// a <c>require not X</c> block is <c>[X]</c> in the executor and <c>[not_X]</c> in MCP.
    /// </summary>
    [Test]
    [Arguments(PolicyBlockDsl, "Person", "Activate", 10L, "IsAdult", "IsAdult")]
    [Arguments(SectionDsl, "Enrollment", "Confirm", null, "not_SectionFull", "not_SectionFull")]
    [Arguments(SectionDsl, "Enrollment", "Confirm", 1L, "SectionFull", "not_SectionFull")]
    public async Task FailedGuards_ExecutorAndMcpMapper_AgreeExceptOnRequireNotPolicyBlocks(
        string dsl, string entityName, string actionName, long? seats, string executorGuard, string mcpGuard) {
        var domain = Lower(dsl);
        var executor = new ModuleInstanceExecutor(domain);
        var subject = executor.Create(entityName, entityName == "Person" ? Values(("Age", 10L)) : Values()).Instance!;
        if (entityName == "Enrollment" && seats is { } taken) {
            var section = executor.Create("Section", Values()).Instance!;
            executor.Link("section", subject, section);
            section.Bag["SeatsTaken"] = taken;
        }

        var blocked = executor.Invoke(subject, actionName);

        var entity = domain.Types.OfType<Entity>().Single(e => e.Name == entityName);
        var action = entity.Actions.Concat(entity.Stages.SelectMany(stage => stage.Actions)).Single(a => a.Name == actionName);
        var mapped = ((IReadOnlyList<string> Guards, string? Message)?)typeof(RuntimeTool)
            .GetMethod("MapGuardFailure", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [actionName, action, blocked.Error]);

        await Assert.That(string.Join(",", blocked.FailedGuards)).IsEqualTo(executorGuard);
        await Assert.That(mapped is { } m ? string.Join(",", m.Guards) : null).IsEqualTo(mcpGuard);
    }

    private const string OwnedKidDsl = """
        domain Lab
        Child: entity { Name: Text }
        Parent: entity { kid: owned Child }
        """;

    [Test]
    public async Task Link_SecondOwnerOfOwnedTarget_FailureIsLinkKindWithTheEntityPath() {
        var executor = ExecutorFor(OwnedKidDsl);
        var parent = executor.Create("Parent", Values()).Instance!;
        var first = executor.Create("Child", Values(("Name", "a"))).Instance!;
        var second = executor.Create("Child", Values(("Name", "b"))).Instance!;
        await Assert.That(executor.Link("kid", parent, first).Succeeded).IsTrue();

        var refused = executor.Link("kid", parent, second);

        await Assert.That(refused.Succeeded).IsFalse();
        await Assert.That(refused.Failure!.Kind).IsEqualTo(DomainFailureKind.Link);
        await Assert.That(refused.Failure.ElementPath).IsEqualTo("Parent.kid");
        await Assert.That(refused.Failure.Guard).IsNull();
        await Assert.That(refused.Error).IsEqualTo(Relationship.LinkViolationMessage("kid"));
    }

    [Test]
    public async Task Unlink_LastOwnerOfOwnedTarget_FailureIsLinkKindWithTheEntityPath() {
        var executor = ExecutorFor(OwnedKidDsl);
        var parent = executor.Create("Parent", Values()).Instance!;
        var kid = executor.Create("Child", Values(("Name", "a"))).Instance!;
        await Assert.That(executor.Link("kid", parent, kid).Succeeded).IsTrue();

        var refused = executor.Unlink("kid", parent, kid);

        await Assert.That(refused.Succeeded).IsFalse();
        await Assert.That(refused.Failure!.Kind).IsEqualTo(DomainFailureKind.Link);
        await Assert.That(refused.Failure.ElementPath).IsEqualTo("Parent.kid");
        await Assert.That(refused.Failure.Guard).IsNull();
        await Assert.That(refused.Error).IsEqualTo(Relationship.UnlinkViolationMessage("kid"));
    }

    [Test]
    public async Task Invoke_AutomaticStageReentry_ThrowsAlreadyEnteredInThisChain() {
        // The module bounds automatic transitions with a per-trigger set, not
        // DomainEntityInstance.MaxTransitionDepth or its depth message.
        var executor = ExecutorFor("""
            domain Loop
            Item: entity {
              Flag: Boolean default(true)
              Draft: stage {
                Go: action { transition to A }
              }
              A: stage {
                entry { if (Flag is true) { transition to B } }
              }
              B: stage {
                entry { if (Flag is true) { transition to A } }
              }
            }
            """);
        var item = executor.Create("Item", Values()).Instance!;

        var thrown = await Assert.That(() => executor.Invoke(item, "Go"))
            .Throws<InvalidOperationException>();

        await Assert.That(thrown!.Message).Contains("already entered in this chain");
        await Assert.That(thrown.Message).Contains("A");
        await Assert.That(Stage(item)).IsEqualTo("Draft");
    }

    [Test]
    public async Task Invoke_NotifyCascade_ExceedsMaxDepth_ThrowsTheModuleMessage() {
        var executor = ExecutorFor("""
            domain Cycle
            A: entity {
              Draft: stage { }
              Active: stage { }
              Go: action { transition to Active }
              seesB: one B via seesA
              when seesB Active { transition to Active }
            }
            B: entity {
              Draft: stage { }
              Active: stage { }
              seesA: one A
              when seesA Active { transition to Active }
            }
            """);
        var a = executor.Create("A", Values()).Instance!;
        var b = executor.Create("B", Values()).Instance!;
        executor.Link("seesB", a, b);
        executor.Link("seesA", b, a);

        var thrown = await Assert.That(() => executor.Invoke(a, "Go"))
            .Throws<InvalidOperationException>();

        await Assert.That(thrown!.Message).Contains("Subscription cascade");
        await Assert.That(thrown.Message).Contains("16");
        await Assert.That(Stage(a)).IsEqualTo("Draft");
        await Assert.That(Stage(b)).IsEqualTo("Draft");
    }

    [Test]
    public async Task Invoke_ForEachFailure_RollsBackEarlierTargets() {
        var executor = ExecutorFor("""
            domain Shop
            Line: entity {
              Qty: Number
              IsPositive: policy { Qty > 0 }
              Mark: action (amount: Number) require IsPositive {
                assign Qty to amount
              }
            }
            Order: entity {
              Note: Text optional
              lines: many Line
              Run: action {
                for lines as item invoke item.Mark(amount: 100)
                assign Note to "done"
              }
            }
            """);
        var order = executor.Create("Order", Values()).Instance!;
        var good = executor.Create("Line", Values(("Qty", 5L))).Instance!;
        var bad = executor.Create("Line", Values(("Qty", 0L))).Instance!;
        executor.Link("lines", order, good);
        executor.Link("lines", order, bad);

        var outcome = executor.Invoke(order, "Run");

        await Assert.That(ReferenceEquals(((System.Collections.IList)order.Bag["_lines"]!)[0], good.Bag)).IsTrue();
        await Assert.That(outcome.Succeeded).IsFalse();
        await Assert.That(outcome.Error).Contains("blocked by policy 'IsPositive'");
        await Assert.That(good.Bag["Qty"]).IsEqualTo(5L);
        await Assert.That(bad.Bag["Qty"]).IsEqualTo(0L);
        await Assert.That(order.Bag["Note"]).IsNull();
    }

    [Test]
    public async Task Invoke_OnEntryConstraintThrow_RollsBackStageAndSkipsNotify() {
        var executor = ExecutorFor("""
            domain Lab
            Widget: entity {
              Score: Number range(1, 10) default(5)
              Bump: Number default(100)
              watcher: Watcher via widgets
              Draft: stage {
                Go: action { transition to Active }
              }
              Active: stage {
                entry { assign Score to Bump }
              }
            }
            Watcher: entity {
              Hits: Number default(0)
              widgets: many Widget
              when widgets Active {
                assign Hits to Hits + 1
              }
            }
            """);
        var widget = executor.Create("Widget", Values()).Instance!;
        var watcher = executor.Create("Watcher", Values()).Instance!;
        executor.Link("widgets", watcher, widget);

        // Inlined into an action, the range check is a DomainResult failure, not a
        // throw. The executor rolls the transition back; the subscriber never runs.
        var outcome = executor.Invoke(widget, "Go");

        await Assert.That(outcome.Succeeded).IsFalse();
        await Assert.That(outcome.Error).Contains("must be <= 10");
        await Assert.That(Stage(widget)).IsEqualTo("Draft");
        await Assert.That(widget.Bag["Score"]).IsEqualTo(5L);
        await Assert.That(watcher.Bag["Hits"]).IsEqualTo(0L);
    }

    [Test]
    public async Task Create_WithEntityIdCollection_ResolvesIdsToBagsAndRegistersSubscribers() {
        var executor = ExecutorFor(Watch);
        var item = executor.Create("Item", Values(("Code", "I1"))).Instance!;

        var watcher = executor.Create("Watcher", Values(("items", new List<object?> { item.Id }))).Instance!;

        await Assert.That(executor.IsLinked("items", watcher, item)).IsTrue();
        executor.Invoke(item, "Activate");
        await Assert.That(watcher.Bag["Hits"]).IsEqualTo(1L);

        var missing = executor.Create("Watcher", Values(("items", new List<object?> { "no-such-id" })));
        await Assert.That(missing.Succeeded).IsFalse();
        await Assert.That(missing.Error).Contains("not found");
    }

    /// <summary>L-08b: on the create door an explicit null is the absence of a value, so a defaulted property takes its default.</summary>
    [Test]
    public async Task Create_ExplicitNullForDefaultedNumber_GetsDefault() {
        var executor = ExecutorFor("""
            domain Shop
            Item: entity {
              Name: Text
              Qty: Number default(5)
            }
            """);

        var outcome = executor.Create("Item", Values(("Name", "a"), ("Qty", null)));

        await Assert.That(outcome.Succeeded).IsTrue().Because(outcome.Error ?? "");
        await Assert.That(outcome.Instance!.Bag["Qty"]).IsEqualTo(5L);
    }

    private static ModuleInstanceExecutor ExecutorFor(string poly) =>
        new(Lower(poly));

    private static Dictionary<string, object?> Values(params (string Name, object? Value)[] values) =>
        values.ToDictionary(v => v.Name, v => v.Value, StringComparer.Ordinal);

    private static string? Stage(ModuleInstance instance) =>
        instance.Bag.TryGetValue("CurrentStage", out var stage) && stage is not null
            ? Convert.ToString(stage, System.Globalization.CultureInfo.InvariantCulture)
            : null;

    /// <summary>Analyzed domain with the storage library loaded and the module cached.</summary>
    private static Domain Lower(string poly) {
        var builder = SessionBuilder.CreateEmpty();
        builder.LoadCore();
        foreach (var id in ExtensionCatalog.ProductAuthoring)
            builder.Load(ExtensionCatalog.Core.Resolve(id));
        builder.Load(new PersistenceEmitLibrary());
        var session = builder.Build();
        var changes = new PolyDslParser(poly, session).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .ApplyAndAnalyze(changes, session: session);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ", result.Analysis.Diagnostics
                .Where(d => d.Severity == Poly.Analysis.DiagnosticSeverity.Error)
                .Select(d => d.Message)));
        _ = RuntimeAnalysisCache.GetOrLower(result.Root!, session, result.Analysis);
        return result.Root!;
    }
}