using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;
using Poly.Interpretation.CSharp;

namespace Poly.Tests.DomainModeling.Lowering;

public class StoreBindCreateTests {
    [Test]
    public async Task CreateIn_Runtime_LowersToCreateInNotCreateInNav() {
        var (domain, analysis) = Evolve("""
            domain Parking
            Permit: entity { Plate: Text unique required }
            Lot: entity {
              permits: many Permit
              Issue: action (plate: Text) {
                create in permits { Plate: plate }
              }
            }
            """);
        var lot = domain.Types.OfType<Entity>().First(e => e.Name == "Lot");
        var action = lot.Actions.First(a => a.Name == "Issue");
        var pass = new EffectLoweringPass(lot, new LoweringContext(
            new Parameter("entity", new TypeReference(lot.Name)),
            Analysis: analysis,
            Domain: domain));
        var lowered = pass.LowerActionBody(action.Effects);
        await Assert.That(lowered).IsNotNull();
        var names = Flatten(lowered!).OfType<Invoke>()
            .Select(i => (i.Delegate as Member)?.MemberName)
            .Where(n => n is not null)
            .ToList();
        await Assert.That(names.Contains("CreateIn")).IsTrue();
        await Assert.That(names.Contains("CreateInNav")).IsFalse();
        await Assert.That(names.Contains("CreateByType")).IsFalse();
    }

    [Test]
    public async Task CreateByType_Runtime_LowersToCreate() {
        var (domain, analysis) = Evolve("""
            domain Shop
            Order: entity {
              Place: action {
                create Order { }
              }
            }
            """);
        var order = domain.Types.OfType<Entity>().First(e => e.Name == "Order");
        var action = order.Actions.First(a => a.Name == "Place");
        var pass = new EffectLoweringPass(order, new LoweringContext(
            new Parameter("entity", new TypeReference(order.Name)),
            Analysis: analysis,
            Domain: domain));
        var lowered = pass.LowerActionBody(action.Effects);
        var names = Flatten(lowered!).OfType<Invoke>()
            .Select(i => (i.Delegate as Member)?.MemberName)
            .ToList();
        await Assert.That(names.Contains("Create")).IsTrue();
        await Assert.That(names.Contains("CreateByType")).IsFalse();
    }

    [Test]
    public async Task CreateIn_Export_UsesCreateIn() {
        var (domain, analysis) = Evolve("""
            domain Parking
            Permit: entity { Plate: Text required }
            Lot: entity {
              permits: many Permit
              Issue: action (plate: Text) {
                create in permits { Plate: plate }
              }
            }
            """);
        var lot = domain.Types.OfType<Entity>().First(e => e.Name == "Lot");
        var action = lot.Actions.First(a => a.Name == "Issue");
        var pass = new EffectLoweringPass(lot, new LoweringContext(
            new ThisReference(),
            UseThisReference: true,
            Analysis: analysis,
            Domain: domain,
            ActionParameterNames: ["plate"]));
        var lowered = pass.LowerActionBody(action.Effects);
        var cs = new CSharpGenerator().Generate(lowered!);
        await Assert.That(cs).Contains("CreateIn(");
        await Assert.That(cs).DoesNotContain("CreatePermits(");
    }

    [Test]
    public async Task CreateIn_EmitAndRuntime_ShareCreateInInvoke() {
        var (domain, analysis) = Evolve("""
            domain Parking
            Permit: entity { Plate: Text required }
            Lot: entity {
              permits: many Permit
              Issue: action (plate: Text) {
                create in permits { Plate: plate }
              }
            }
            """);
        var lot = domain.Types.OfType<Entity>().First(e => e.Name == "Lot");
        var action = lot.Actions.First(a => a.Name == "Issue");
        var runtime = new EffectLoweringPass(lot, new LoweringContext(
            new Parameter("entity", new TypeReference(lot.Name)),
            Analysis: analysis,
            Domain: domain,
            ActionParameterNames: ["plate"]));
        var emit = new EffectLoweringPass(lot, new LoweringContext(
            new ThisReference(),
            UseThisReference: true,
            Analysis: analysis,
            Domain: domain,
            ActionParameterNames: ["plate"]));
        Invoke Name(Node tree) => Flatten(tree).OfType<Invoke>()
            .First(i => i.Delegate is Member { MemberName: "CreateIn" });
        var runtimeInvoke = Name(runtime.LowerActionBody(action.Effects)!);
        var emitInvoke = Name(emit.LowerActionBody(action.Effects)!);
        await Assert.That((runtimeInvoke.Delegate as Member)!.MemberName)
            .IsEqualTo((emitInvoke.Delegate as Member)!.MemberName);
        await Assert.That((runtimeInvoke.Arguments[0] as Constant)?.Value)
            .IsEqualTo((emitInvoke.Arguments[0] as Constant)?.Value);
        await Assert.That(runtimeInvoke.Arguments.Length).IsEqualTo(emitInvoke.Arguments.Length);
    }

    [Test]
    public async Task Store_CreateIn_RegistersChildAndLink() {
        var (domain, _) = Evolve("""
            domain Parking
            Permit: entity { Plate: Text unique required }
            Lot: entity { permits: many Permit }
            """);
        var lotE = domain.Types.OfType<Entity>().First(e => e.Name == "Lot");
        var store = new DomainInstanceStore();
        var lot = DomainEntityInstance.Create(lotE, domain: domain);
        store.Add(lot);

        var result = store.CreateIn(lot, "permits",
            new Dictionary<string, object?> { ["Plate"] = "ABC123" });
        await Assert.That(result.IsSuccess).IsTrue();
        var child = result.Value as DomainEntityInstance;
        await Assert.That(child).IsNotNull();
        await Assert.That(store.GetRelatedInstances("permits", lot)).Contains(child!);
        await Assert.That(lot.CreatedChildren).Contains(child!);
    }

    [Test]
    public async Task Store_CreateIn_UniqueCollision_IsFailureWithoutRegistering() {
        var (domain, _) = Evolve("""
            domain Parking
            Permit: entity { Plate: Text unique required }
            Lot: entity { permits: many Permit }
            """);
        var lotE = domain.Types.OfType<Entity>().First(e => e.Name == "Lot");
        var permitE = domain.Types.OfType<Entity>().First(e => e.Name == "Permit");
        var store = new DomainInstanceStore();
        var lot = DomainEntityInstance.Create(lotE, domain: domain);
        var existing = DomainEntityInstance.Create(permitE,
            new Dictionary<string, object?> { ["Plate"] = "ABC123" }, domain);
        store.Add(lot);
        store.Add(existing);

        var result = store.CreateIn(lot, "permits",
            new Dictionary<string, object?> { ["Plate"] = "ABC123" });
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.ErrorMessage).Contains("Unique");
        await Assert.That(lot.CreatedChildren).IsEmpty();
        await Assert.That(store.GetRelatedInstances("permits", lot)).IsEmpty();
    }

    [Test]
    public async Task CreateThenRelExists_SameAction_SeesChild() {
        var (domain, _) = Evolve("""
            domain Parking
            Permit: entity { Plate: Text required }
            Lot: entity {
              Occupied: Number default(0)
              permits: many Permit
              Issue: action (plate: Text) {
                create in permits { Plate: plate }
                if (permits exists) {
                  assign Occupied to 1
                }
              }
            }
            """);
        var lotE = domain.Types.OfType<Entity>().First(e => e.Name == "Lot");
        var store = new DomainInstanceStore();
        var lot = DomainEntityInstance.Create(lotE, domain: domain);
        store.Add(lot);
        var result = lot.InvokeAction("Issue",
            new Dictionary<string, object?> { ["plate"] = "ABC123" });
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(lot.GetProperty<object>("Occupied")).IsEqualTo(1L);
        await Assert.That(lot.CreatedChildren.Count).IsEqualTo(1);
    }

    [Test]
    public async Task MixedIfCreateIn_Runtime_CompilesAsOneTree() {
        var (domain, analysis) = Evolve("""
            domain Parking
            Permit: entity { Plate: Text required }
            Lot: entity {
              Open: Boolean default(true)
              permits: many Permit
              Issue: action (plate: Text) {
                if (Open) {
                  create in permits { Plate: plate }
                }
              }
            }
            """);
        var lot = domain.Types.OfType<Entity>().First(e => e.Name == "Lot");
        var action = lot.Actions.First(a => a.Name == "Issue");
        var pass = new EffectLoweringPass(lot, new LoweringContext(
            new Parameter("entity", new TypeReference(lot.Name)),
            Analysis: analysis,
            Domain: domain));
        var lowered = pass.LowerActionBody(action.Effects);
        await Assert.That(Flatten(lowered!).Any(n => n is IfStatement)).IsTrue();
        await Assert.That(Flatten(lowered!).OfType<Invoke>().Any(i =>
            i.Delegate is Member { MemberName: "CreateIn" })).IsTrue();
    }

    [Test]
    public async Task RelExists_Runtime_LowersToExistsRelated() {
        var (domain, analysis) = Evolve("""
            domain Shop
            Order: entity { Code: Text required }
            Customer: entity {
              orders: many Order
              HasOrders: policy { orders exists }
            }
            """);
        var customer = domain.Types.OfType<Entity>().First(e => e.Name == "Customer");
        var policy = customer.Policies.First(p => p.Name == "HasOrders");
        var pass = new DomainExpressionLoweringPass(new LoweringContext(
            new Parameter("entity", new TypeReference(customer.Name)),
            Analysis: analysis,
            Domain: domain,
            NavigationNameResolver: EffectLoweringPass.BuildNavigationNameResolver(customer, domain, analysis),
            IsCollectionNavigation: EffectLoweringPass.BuildIsCollectionNavigation(customer, domain, analysis),
            IsRelationshipNavigation: EffectLoweringPass.BuildIsRelationshipNavigation(customer, domain, analysis)));
        var lowered = pass.Lower(policy.Expression,
            new Parameter("entity", new TypeReference(customer.Name)));
        var names = Flatten(lowered).OfType<Invoke>()
            .Select(i => (i.Delegate as Member)?.MemberName)
            .ToList();
        await Assert.That(names.Contains("ExistsRelated")).IsTrue();
    }

    [Test]
    public async Task PathPrefix_Runtime_LowersToReadRelated() {
        var (domain, analysis) = Evolve("""
            domain Shop
            Advisor: entity { Name: Text required }
            Customer: entity {
              advisor: Advisor
              AdvisorNamed: policy { advisor Name is "Pat" }
            }
            """);
        var customer = domain.Types.OfType<Entity>().First(e => e.Name == "Customer");
        var policy = customer.Policies.First(p => p.Name == "AdvisorNamed");
        var pass = new DomainExpressionLoweringPass(new LoweringContext(
            new Parameter("entity", new TypeReference(customer.Name)),
            Analysis: analysis,
            Domain: domain,
            NavigationNameResolver: EffectLoweringPass.BuildNavigationNameResolver(customer, domain, analysis),
            IsRelationshipNavigation: EffectLoweringPass.BuildIsRelationshipNavigation(customer, domain, analysis)));
        var lowered = pass.Lower(policy.Expression,
            new Parameter("entity", new TypeReference(customer.Name)));
        var names = Flatten(lowered).OfType<Invoke>()
            .Select(i => (i.Delegate as Member)?.MemberName)
            .ToList();
        await Assert.That(names.Contains("GetRelatedOne")).IsTrue();
    }

    [Test]
    public async Task CreateIn_EntityInitializer_Runtime_CastsValueToObject() {
        var (domain, analysis) = Evolve("""
            domain CreateNavBinding
            Book: entity { Title: Text }
            Patron: entity {
              loans: many Loan
              CheckOut: action (book: Book) {
                create in loans { book: book }
              }
            }
            Loan: entity {
              book: Book
              borrower: Patron
            }
            """);
        var patron = domain.Types.OfType<Entity>().First(e => e.Name == "Patron");
        var action = patron.Actions.First(a => a.Name == "CheckOut");
        var pass = new EffectLoweringPass(patron, new LoweringContext(
            new Parameter("entity", new TypeReference(patron.Name)),
            Analysis: analysis,
            Domain: domain,
            ActionParameterNames: ["book"]));
        var lowered = pass.LowerActionBody(action.Effects);
        var createIn = Flatten(lowered!).OfType<Invoke>()
            .First(i => i.Delegate is Member { MemberName: "CreateIn" });
        await Assert.That(createIn.Arguments.Last()).IsTypeOf<TypeCast>();
    }

    [Test]
    public async Task CreateIn_ScalarInitializer_Runtime_DoesNotCastToObject() {
        var (domain, analysis) = Evolve("""
            domain Parking
            Permit: entity { Plate: Text required }
            Lot: entity {
              permits: many Permit
              Issue: action (plate: Text) {
                create in permits { Plate: plate }
              }
            }
            """);
        var lot = domain.Types.OfType<Entity>().First(e => e.Name == "Lot");
        var action = lot.Actions.First(a => a.Name == "Issue");
        var pass = new EffectLoweringPass(lot, new LoweringContext(
            new Parameter("entity", new TypeReference(lot.Name)),
            Analysis: analysis,
            Domain: domain,
            ActionParameterNames: ["plate"]));
        var lowered = pass.LowerActionBody(action.Effects);
        var createIn = Flatten(lowered!).OfType<Invoke>()
            .First(i => i.Delegate is Member { MemberName: "CreateIn" });
        await Assert.That(createIn.Arguments.Last() is TypeCast).IsFalse();
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
        foreach (var child in node.Children) {
            if (child is null)
                continue;
            foreach (var n in Flatten(child))
                yield return n;
        }
    }
}
