using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;
using Poly.Interpretation.CSharp;

namespace Poly.Tests.DomainModeling.Lowering;

public class StoreBindUniqueTests {
    [Test]
    public async Task UniqueAssign_Runtime_LowersToEnsureUniqueThenAssign() {
        var entity = PermitEntity();
        var pass = new EffectLoweringPass(entity, new LoweringContext(
            new Parameter("entity", new TypeReference(entity.Name))));

        var lowered = pass.TryLowerVmNode(new AssignEffect(
            DomainExpression.Property("Plate"),
            DomainExpression.Property("plate")));

        await Assert.That(lowered).IsNotNull();
        var nodes = Flatten(lowered!).ToList();
        await Assert.That(nodes.Any(n =>
            n is Invoke { Delegate: Member { MemberName: "EnsureUnique" } } inv
            && inv.Arguments is [Constant { Value: "Plate" }, _])).IsTrue();
        await Assert.That(nodes.Any(n =>
            n is Assignment { Destination: Member { MemberName: "Plate" } })).IsTrue();
    }

    [Test]
    public async Task UniqueAssign_Export_LowersToEnsureUniqueThenAssign() {
        var entity = PermitEntity();
        var pass = new EffectLoweringPass(entity, new LoweringContext(
            new ThisReference(),
            UseThisReference: true));

        var lowered = pass.TryLowerVmNode(new AssignEffect(
            DomainExpression.Property("Plate"),
            DomainExpression.Property("plate")));
        var cs = new CSharpGenerator().Generate(lowered!);

        await Assert.That(cs).Contains("EnsureUnique");
        await Assert.That(cs).Contains("Plate");
    }

    [Test]
    public async Task NonUniqueAssign_Runtime_IsBareAssignment() {
        var name = new Property("Name", new DomainTypeReference("Text"), []);
        var entity = new Entity("Person",
            Properties: [name],
            Actions: [], Policies: [], Stages: []);
        var pass = new EffectLoweringPass(entity, new LoweringContext(
            new Parameter("entity", new TypeReference(entity.Name))));

        var lowered = pass.TryLowerVmNode(new AssignEffect(
            DomainExpression.Property("Name"),
            DomainExpression.Literal("Ada")));

        await Assert.That(lowered).IsTypeOf<Assignment>();
    }

    [Test]
    public async Task UniqueAssign_WithStorageBag_LowersToEnsureUnique() {
        var (domain, analysis) = Evolve("""
            domain Parking
            Permit: entity {
              Plate: Text unique required
              Relabel: action (plate: Text) {
                assign Plate to plate
              }
            }
            """);
        var entity = domain.Types.OfType<Entity>().First(e => e.Name == "Permit");
        await Assert.That(analysis.GetMetadata<Poly.DomainModeling.Analysis.StorageMappingMetadata>(domain))
            .IsNotNull();

        var pass = new EffectLoweringPass(entity, new LoweringContext(
            new Parameter("entity", new TypeReference(entity.Name)),
            Analysis: analysis,
            Domain: domain));
        var lowered = pass.TryLowerVmNode(new AssignEffect(
            DomainExpression.Property("Plate"),
            DomainExpression.Property("plate")));

        await Assert.That(Flatten(lowered!).Any(n =>
            n is Invoke { Delegate: Member { MemberName: "EnsureUnique" } })).IsTrue();
    }

    [Test]
    public async Task Store_EnsureUnique_Collision_IsFailureWithoutMutatingCaller() {
        var entity = PermitEntity();
        var store = new DomainInstanceStore();
        var existing = DomainEntityInstance.Create(entity,
            new Dictionary<string, object?> { ["Plate"] = "ABC123" });
        var other = DomainEntityInstance.Create(entity,
            new Dictionary<string, object?> { ["Plate"] = "XYZ999" });
        store.Add(existing);
        store.Add(other);

        var result = store.EnsureUnique(other, "Plate", "ABC123");
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.ErrorMessage).Contains("Unique");
        await Assert.That(other.GetProperty<string>("Plate")).IsEqualTo("XYZ999");
    }

    [Test]
    public async Task Store_EnsureUnique_SelfValue_IsSuccess() {
        var entity = PermitEntity();
        var store = new DomainInstanceStore();
        var existing = DomainEntityInstance.Create(entity,
            new Dictionary<string, object?> { ["Plate"] = "ABC123" });
        store.Add(existing);

        var result = store.EnsureUnique(existing, "Plate", "ABC123");
        await Assert.That(result.IsSuccess).IsTrue();
    }

    [Test]
    public async Task Store_EnsureUnique_NonUniqueProperty_IsSuccess() {
        var name = new Property("Name", new DomainTypeReference("Text"), []);
        var entity = new Entity("Person",
            Properties: [name],
            Actions: [], Policies: [], Stages: []);
        var store = new DomainInstanceStore();
        var a = DomainEntityInstance.Create(entity,
            new Dictionary<string, object?> { ["Name"] = "Ada" });
        var b = DomainEntityInstance.Create(entity,
            new Dictionary<string, object?> { ["Name"] = "Grace" });
        store.Add(a);
        store.Add(b);

        var result = store.EnsureUnique(b, "Name", "Ada");
        await Assert.That(result.IsSuccess).IsTrue();
    }

    [Test]
    public async Task ConditionalUniqueAssign_Runtime_LowersEnsureUniqueInsideIf() {
        var (domain, analysis) = Evolve("""
            domain Parking
            Permit: entity {
              Plate: Text unique required
              Relabel: action (plate: Text) {
                if (plate != "") {
                  assign Plate to plate
                }
              }
            }
            """);
        var entity = domain.Types.OfType<Entity>().First(e => e.Name == "Permit");
        var action = entity.Actions.First(a => a.Name == "Relabel");
        var pass = new EffectLoweringPass(entity, new LoweringContext(
            new Parameter("entity", new TypeReference(entity.Name)),
            Analysis: analysis,
            Domain: domain));
        var lowered = pass.LowerActionBody(action.Effects);
        var cs = new CSharpGenerator().Generate(lowered!);
        await Assert.That(cs).Contains("EnsureUnique");
        await Assert.That(Flatten(lowered!).Any(n => n is IfStatement)).IsTrue();
    }

    [Test]
    public async Task UniqueAssign_WithoutStore_SucceedsWhenNoPeers() {
        var entity = PermitWithRelabel();
        var domain = DomainTestFactory.Create("Parking", [entity]);
        var instance = DomainEntityInstance.Create(entity,
            new Dictionary<string, object?> { ["Plate"] = "XYZ999" }, domain: domain);

        var result = instance.InvokeAction("Relabel",
            new Dictionary<string, object?> { ["plate"] = "ABC123" });
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(instance.GetProperty<string>("Plate")).IsEqualTo("ABC123");
    }

    private static Entity PermitEntity() {
        var plate = new Property("Plate", new DomainTypeReference("Text"), [new UniqueConstraint()]);
        return new Entity("Permit",
            Properties: [plate],
            Actions: [], Policies: [], Stages: []);
    }

    private static Entity PermitWithRelabel() {
        var plate = new Property("Plate", new DomainTypeReference("Text"), [new UniqueConstraint()]);
        var relabel = new Poly.DomainModeling.Ontology.Action(
            "Relabel",
            InvocationResult.Void,
            [new Property("plate", new DomainTypeReference("Text"), [])],
            [new AssignEffect(DomainExpression.Property("Plate"), DomainExpression.Property("plate"))],
            []);
        return new Entity("Permit",
            Properties: [plate],
            Actions: [relabel], Policies: [], Stages: []);
    }

    private static (Domain Domain, AnalysisResult Analysis) Evolve(string poly) {
        var changes = new PolyDslParser(poly).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(changes);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ",
                result.Analysis.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.Message)));
        var analysis = DomainModelAnalyzer.Analyze(result.Root!);
        return (result.Root!, analysis);
    }

    private static IEnumerable<Node> Flatten(Node node) {
        yield return node;
        switch (node) {
            case Block b:
                foreach (var child in b.Nodes)
                    foreach (var n in Flatten(child))
                        yield return n;
                break;
            case IfStatement ifStmt:
                foreach (var n in Flatten(ifStmt.ThenBranch))
                    yield return n;
                if (ifStmt.ElseBranch is not null)
                    foreach (var n in Flatten(ifStmt.ElseBranch))
                        yield return n;
                break;
            case Assignment a:
                foreach (var n in Flatten(a.Value))
                    yield return n;
                foreach (var n in Flatten(a.Destination))
                    yield return n;
                break;
            case Invoke inv:
                foreach (var arg in inv.Arguments)
                    foreach (var n in Flatten(arg))
                        yield return n;
                foreach (var n in Flatten(inv.Delegate))
                    yield return n;
                break;
        }
    }
}
