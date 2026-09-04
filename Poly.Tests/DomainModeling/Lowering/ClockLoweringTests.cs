using Poly.DomainModeling;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;

namespace Poly.Tests.DomainModeling.Lowering;

public class ClockLoweringTests {
    [Test]
    public async Task Assign_DateToNow_LowersToFromDateTime_NotLiteral() {
        var (entity, action) = DateAssignAction(DomainExpression.Property("Now"));
        var lowered = Lower(entity, action);
        var assign = Flatten(lowered).OfType<Assignment>()
            .First(a => a.Destination is Member { MemberName: "Due" });
        await Assert.That(assign.Value is Constant).IsFalse();
        var members = Flatten(assign.Value).OfType<Member>().Select(m => m.MemberName).ToList();
        await Assert.That(members.Contains("FromDateTime")).IsTrue();
        await Assert.That(members.Contains("UtcNow")).IsTrue();
    }

    [Test]
    public async Task Assign_DateToNowIr_LowersToFromDateTime_NotLiteral() {
        var (entity, action) = DateAssignAction(new Now());
        var lowered = Lower(entity, action);
        var assign = Flatten(lowered).OfType<Assignment>()
            .First(a => a.Destination is Member { MemberName: "Due" });
        await Assert.That(assign.Value is Constant).IsFalse();
        var members = Flatten(assign.Value).OfType<Member>().Select(m => m.MemberName).ToList();
        await Assert.That(members.Contains("FromDateTime")).IsTrue();
        await Assert.That(members.Contains("UtcNow")).IsTrue();
    }

    [Test]
    public async Task Assign_DateToNowIr_StoresDateOnly() {
        var (entity, _) = DateAssignAction(new Now());
        var domain = DomainTestFactory.Create("Clocks", [entity]);
        var instance = DomainEntityInstance.Create(entity, domain: domain);
        var result = instance.InvokeAction("Touch");
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(instance.GetProperty<object>("Due")).IsTypeOf<DateOnly>();
    }

    [Test]
    public async Task CreateIn_DateNow_StoresDateOnly() {
        var permit = new Entity("Permit",
            Properties: [
                new Property("Plate", new DomainTypeReference("Text"), []),
                new Property("Issued", new DomainTypeReference("Date"), []),
            ],
            Actions: [],
            Policies: [],
            Stages: []);
        var issue = new Poly.DomainModeling.Ontology.Action("Issue", InvocationResult.Void,
            Parameters: [new Property("plate", new DomainTypeReference("Text"), [])],
            Effects: [
                new CreateEntityInRelationshipEffect("permits", [
                    new PropertyBinding("Plate", DomainExpression.Property("plate")),
                    new PropertyBinding("Issued", DomainExpression.Property("Now")),
                ])
            ],
            Policies: []);
        var lot = new Entity("Lot",
            Properties: [],
            Actions: [issue],
            Policies: [],
            Stages: []);
        var rel = new Relationship("permits",
            new DomainTypeReference("Lot"), new DomainTypeReference("Permit"),
            RelationshipCardinality.OneToMany, []);
        var domain = DomainTestFactory.Create("Parking", [permit, lot], [rel]);
        var store = new DomainInstanceStore();
        var instance = DomainEntityInstance.Create(lot, domain: domain);
        store.Add(instance);

        var result = instance.InvokeAction("Issue",
            new Dictionary<string, object?> { ["plate"] = "ABC123" });
        await Assert.That(result.Succeeded).IsTrue();
        var child = instance.CreatedChildren.Single();
        await Assert.That(child.GetProperty<object>("Issued")).IsTypeOf<DateOnly>();
        await Assert.That(child.GetProperty<string>("Plate")).IsEqualTo("ABC123");
    }

    [Test]
    public async Task Assign_TextToGuid_StoresStringNotLiteralInTree() {
        var id = new Property("ExternalId", new DomainTypeReference("Text"), []);
        var stamp = new Poly.DomainModeling.Ontology.Action("Stamp", InvocationResult.Void, [],
            [new AssignEffect(DomainExpression.Property("ExternalId"), DomainExpression.Property("Guid"))],
            []);
        var entity = new Entity("Item", [id], [stamp], [], []);
        var lowered = Lower(entity, stamp);
        var assign = Flatten(lowered).OfType<Assignment>()
            .First(a => a.Destination is Member { MemberName: "ExternalId" });
        await Assert.That(assign.Value is Constant).IsFalse();

        var domain = DomainTestFactory.Create("Ids", [entity]);
        var instance = DomainEntityInstance.Create(entity, domain: domain);
        var result = instance.InvokeAction("Stamp");
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(instance.GetProperty<object>("ExternalId")).IsTypeOf<string>();
        await Assert.That(Guid.TryParse(instance.GetProperty<string>("ExternalId"), out _)).IsTrue();
    }

    private static (Entity Entity, Poly.DomainModeling.Ontology.Action Action) DateAssignAction(
        DomainExpression clock) {
        var due = new Property("Due", new DomainTypeReference("Date"), []);
        var touch = new Poly.DomainModeling.Ontology.Action("Touch", InvocationResult.Void, [],
            [new AssignEffect(DomainExpression.Property("Due"), clock)], []);
        var entity = new Entity("Item", [due], [touch], [], []);
        return (entity, touch);
    }

    private static Node Lower(Entity entity, Poly.DomainModeling.Ontology.Action action) {
        var pass = new EffectLoweringPass(entity, new LoweringContext(
            new Parameter("entity", new TypeReference(entity.Name))));
        var lowered = pass.LowerActionBody(action.Effects);
        return lowered ?? throw new InvalidOperationException("LowerActionBody returned null.");
    }

    private static IEnumerable<Node> Flatten(Node node) {
        yield return node;
        foreach (var child in node.Children) {
            if (child is null)
                continue;
            foreach (var n in Flatten(child))
                yield return n;
        }
    }
}
