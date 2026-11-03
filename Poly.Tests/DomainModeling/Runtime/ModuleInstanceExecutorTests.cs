using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Libraries.Storage;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;

namespace Poly.Tests.DomainModeling.Runtime;

/// <summary>
/// S13.2a (#368): the host seam that runs the lowered module on dictionary
/// instances. Nothing here constructs a <see cref="DomainEntityInstance"/> or a
/// <see cref="DomainInstanceStore"/>.
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

    private static ModuleInstanceExecutor ExecutorFor(string poly) =>
        new(Lower(poly));

    private static Dictionary<string, object?> Values(params (string Name, object? Value)[] values) =>
        values.ToDictionary(v => v.Name, v => v.Value, StringComparer.Ordinal);

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