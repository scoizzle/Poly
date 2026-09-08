using Poly.Analysis;
using Poly.Ast.Nodes;
using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;
using Poly.Interpretation.CSharp;
using Poly.Packs.Sqlite;

namespace Poly.Tests.DomainModeling.Compile;

public class PipelineTransformationTests {
    [Test]
    public async Task SessionLower_IsTheModuleEmitPrints() {
        var (domain, analysis, session) = Evolve("""
            domain Parking
            Permit: entity { Plate: Text required }
            Lot: entity {
              permits: many Permit
              Issue: action (plate: Text) {
                create in permits { Plate: plate }
              }
            }
            """);
        var module = session.Lower(domain, analysis);
        var again = session.Lower(domain, analysis);
        await Assert.That(ReferenceEquals(module, again)).IsTrue();
        var lot = module.First(t => t.Name == "Lot");
        await Assert.That(lot.Methods?.Any(m => m.Name == "Issue")).IsTrue();
        await Assert.That(lot.Methods?.Any(m => m.Name == "CreateIn")).IsTrue();
    }

    [Test]
    public async Task SessionLower_PopulatesNamedActionMethodBody() {
        var (domain, analysis, session) = Evolve("""
            domain Parking
            Permit: entity { Plate: Text required }
            Lot: entity {
              permits: many Permit
              Issue: action (plate: Text) {
                create in permits { Plate: plate }
              }
            }
            """);
        var module = session.Lower(domain, analysis);
        var lot = module.First(t => t.Name == "Lot");
        var issue = lot.Methods?.FirstOrDefault(m => m.Name == "Issue");
        await Assert.That(issue).IsNotNull();
        await Assert.That(issue!.Body).IsNotNull();
        await Assert.That(RuntimeAnalysisCache.TryGetModuleMethod(domain, "Lot", "Issue", out var cached)).IsTrue();
        await Assert.That(ReferenceEquals(issue, cached)).IsTrue();
    }

    [Test]
    public async Task InvokeAction_UsesTheModuleMethodBody_NotAReloweredCopy() {
        var (domain, analysis, session) = Evolve("""
            domain Parking
            Permit: entity { Plate: Text required }
            Lot: entity {
              permits: many Permit
              Issue: action (plate: Text) {
                create in permits { Plate: plate }
              }
            }
            """);
        var module = session.Lower(domain, analysis);
        var lotType = module.First(t => t.Name == "Lot");
        var before = lotType.Methods?.First(m => m.Name == "Issue").Body;
        await Assert.That(before).IsNotNull();

        var lotE = domain.Types.OfType<Entity>().First(e => e.Name == "Lot");
        var store = new DomainInstanceStore();
        var lot = DomainEntityInstance.Create(lotE, domain: domain);
        store.Add(lot);
        var cs = new CSharpGenerator().Generate(before!);
        var result = lot.InvokeAction("Issue", new Dictionary<string, object?> { ["plate"] = "AAA" });
        if (!result.Succeeded)
            throw new InvalidOperationException((result.ErrorMessage ?? "Issue failed") + " BODY:\n" + cs);
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(lot.CreatedChildren.Count).IsEqualTo(1);

        await Assert.That(RuntimeAnalysisCache.TryGetModuleMethod(domain, "Lot", "Issue", out var afterMethod)).IsTrue();
        await Assert.That(ReferenceEquals(before, afterMethod!.Body)).IsTrue();
    }

    [Test]
    public async Task InvokeAction_RunsTheModuleMethodBody_NotAReloweredEffectWalk() {
        var (domain, analysis, session) = Evolve("""
            domain Parking
            Permit: entity { Plate: Text required }
            Lot: entity {
              permits: many Permit
              Issue: action (plate: Text) {
                create in permits { Plate: plate }
              }
            }
            """);
        var module = session.Lower(domain, analysis);
        var lotType = module.First(t => t.Name == "Lot");
        if (lotType.Methods is not IList<MethodDefinitionNode> methods)
            throw new InvalidOperationException("Module methods must be a mutable list.");
        var index = -1;
        for (var i = 0; i < methods.Count; i++) {
            if (string.Equals(methods[i].Name, "Issue", StringComparison.Ordinal)) {
                index = i;
                break;
            }
        }
        await Assert.That(index).IsGreaterThanOrEqualTo(0);
        methods[index] = methods[index] with {
            Body = new Block([
                new Return(new Invoke(new Member(new NamedTypeReference("DomainResult"), "Success")))
            ])
        };

        var lotE = domain.Types.OfType<Entity>().First(e => e.Name == "Lot");
        var store = new DomainInstanceStore();
        var lot = DomainEntityInstance.Create(lotE, domain: domain);
        store.Add(lot);
        var result = lot.InvokeAction("Issue", new Dictionary<string, object?> { ["plate"] = "AAA" });
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(lot.CreatedChildren.Count).IsEqualTo(0);
    }

    [Test]
    public async Task InvokeAction_MissingModuleMethod_Throws_DoesNotRelower() {
        // Ontology residual stop: named invoke never falls back to LowerActionBody.
        var (domain, analysis, session) = Evolve("""
            domain Parking
            Permit: entity { Plate: Text required }
            Lot: entity {
              permits: many Permit
              Issue: action (plate: Text) {
                create in permits { Plate: plate }
              }
            }
            """);
        var module = session.Lower(domain, analysis);
        var lotType = module.First(t => t.Name == "Lot");
        if (lotType.Methods is not IList<MethodDefinitionNode> methods)
            throw new InvalidOperationException("Module methods must be a mutable list.");
        for (var i = methods.Count - 1; i >= 0; i--) {
            if (string.Equals(methods[i].Name, "Issue", StringComparison.Ordinal))
                methods.RemoveAt(i);
        }
        await Assert.That(RuntimeAnalysisCache.TryGetModuleMethod(domain, "Lot", "Issue", out _))
            .IsFalse();

        var lotE = domain.Types.OfType<Entity>().First(e => e.Name == "Lot");
        var store = new DomainInstanceStore();
        var lot = DomainEntityInstance.Create(lotE, domain: domain);
        store.Add(lot);
        await Assert.That(() =>
                lot.InvokeAction("Issue", new Dictionary<string, object?> { ["plate"] = "AAA" }))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("Module method 'Issue' is missing");
        await Assert.That(lot.CreatedChildren.Count).IsEqualTo(0);
    }

    [Test]
    public async Task InvokeAction_SecondCall_UsesCachedOperationBody() {
        var (domain, _, _) = Evolve("""
            domain Parking
            Permit: entity { Plate: Text required }
            Lot: entity {
              permits: many Permit
              Issue: action (plate: Text) {
                create in permits { Plate: plate }
              }
            }
            """);
        var lotE = domain.Types.OfType<Entity>().First(e => e.Name == "Lot");
        var store = new DomainInstanceStore();
        var lot = DomainEntityInstance.Create(lotE, domain: domain);
        store.Add(lot);
        var first = lot.InvokeAction("Issue", new Dictionary<string, object?> { ["plate"] = "AAA" });
        var second = lot.InvokeAction("Issue", new Dictionary<string, object?> { ["plate"] = "BBB" });
        await Assert.That(first.Succeeded).IsTrue();
        await Assert.That(second.Succeeded).IsTrue();
        await Assert.That(lot.CreatedChildren.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Analyze_WithSqlite_GetOrAnalyzeSeesSqliteMaps() {
        var catalog = ExtensionCatalog.Core.With(new SqliteLibrary());
        var poly = """
            domain Parking
            uses sqlite
            Lot: entity { Name: Text }
            """;
        var session = DomainSession.ForSource(poly, ExtensionCatalog.ProductAuthoring, catalog);
        var changes = new PolyDslParser(poly, session).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(changes, session: session);
        await Assert.That(result.Succeeded).IsTrue();
        var domain = result.Root!;
        var analysis = session.Analyze(domain);
        var cached = RuntimeAnalysisCache.GetOrAnalyze(domain);
        await Assert.That(ReferenceEquals(analysis, cached)).IsTrue();
        await Assert.That(RuntimeAnalysisCache.Session(domain).TypeMaps.ToSqlColumnType("Text"))
            .IsEqualTo("TEXT");
        await Assert.That(analysis.GetMetadata<StorageMappingMetadata>(domain)).IsNotNull();
    }

    [Test]
    public async Task InvokeAction_ContractBound_FailsClosedWithoutInProcessAdapter() {
        var (domain, analysis, session) = Evolve("""
            domain Shop
            Order: entity {
              Total: Number default(0)
              Pay: action (request: ChargeRequest) {
                assign Total to Total
              }
            }

            Stripe: contract external stripe v1 {
              ChargeRequest: value {
                Amount: Number
                Currency: Text
              }
              Charge: outbound operation ChargeRequest
            }

            ChargeOrder: bind Stripe Charge to Pay request
            """);
        session.Lower(domain, analysis);
        var orderE = domain.Types.OfType<Entity>().First(e => e.Name == "Order");
        var store = new DomainInstanceStore();
        var order = DomainEntityInstance.Create(orderE, domain: domain);
        store.Add(order);
        var request = new Dictionary<string, object?> { ["Amount"] = 10L, ["Currency"] = "USD" };
        var result = order.InvokeAction("Pay",
            new Dictionary<string, object?> { ["request"] = request });
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.ErrorMessage).Contains("Stripe.Charge");
        await Assert.That(result.ErrorMessage!).Contains("no in-process adapter");
    }

    [Test]
    public async Task ConditionalInvoke_RunsOnTrueBranch_AndPrintsInModule() {
        var (domain, analysis, session) = Evolve("""
            domain Counter
            Item: entity {
              Qty: Number default(0)
              Inc: action { assign Qty to Qty + 1 }
              Maybe: action {
                if (Qty is 0) { invoke Inc }
              }
            }
            """);
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.NestedDirectEffectDropped)).IsFalse();

        var module = session.Lower(domain, analysis);
        var itemType = module.First(t => t.Name == "Item");
        var maybe = itemType.Methods?.FirstOrDefault(m => m.Name == "Maybe");
        await Assert.That(maybe?.Body).IsNotNull();
        var bodyCs = new CSharpGenerator().Generate(maybe!.Body!);
        await Assert.That(bodyCs).Contains("Inc()");

        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var cs = new CSharpGenerator().Generate(new CompilationUnitNode([], null, types, null));
        await Assert.That(cs).Contains("this.Inc()");

        var itemE = domain.Types.OfType<Entity>().First(e => e.Name == "Item");
        var store = new DomainInstanceStore();
        var zero = DomainEntityInstance.Create(itemE, domain: domain);
        store.Add(zero);
        await Assert.That(zero.InvokeAction("Maybe").Succeeded).IsTrue();
        await Assert.That(zero.GetProperty<object>("Qty")).IsEqualTo(1L);

        var already = DomainEntityInstance.Create(itemE,
            new Dictionary<string, object?> { ["Qty"] = 1L }, domain);
        store.Add(already);
        await Assert.That(already.InvokeAction("Maybe").Succeeded).IsTrue();
        await Assert.That(already.GetProperty<object>("Qty")).IsEqualTo(1L);
    }

    [Test]
    public async Task ConditionalCreateIn_DoesNotWarnDropped_AndRuns() {
        var (domain, analysis, session) = Evolve("""
            domain Box
            Token: entity { Kind: Text required }
            Parser: entity {
              tokens: many Token
              Rush: Boolean default(false)
              Lex: action {
                if (Rush is true) {
                  create in tokens { Kind: "rush" }
                }
              }
            }
            """);
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.NestedDirectEffectDropped)).IsFalse();
        session.Lower(domain, analysis);

        var parserE = domain.Types.OfType<Entity>().First(e => e.Name == "Parser");
        var store = new DomainInstanceStore();
        var quiet = DomainEntityInstance.Create(parserE, domain: domain);
        store.Add(quiet);
        await Assert.That(quiet.InvokeAction("Lex").Succeeded).IsTrue();
        await Assert.That(quiet.CreatedChildren).IsEmpty();

        var rush = DomainEntityInstance.Create(parserE,
            new Dictionary<string, object?> { ["Rush"] = true }, domain);
        store.Add(rush);
        await Assert.That(rush.InvokeAction("Lex").Succeeded).IsTrue();
        await Assert.That(rush.CreatedChildren.Count).IsEqualTo(1);
    }

    private static (Domain Domain, AnalysisResult Analysis, DomainSession Session) Evolve(string poly) {
        var session = DomainSession.ForSource(poly, ExtensionCatalog.ProductAuthoring);
        var changes = new PolyDslParser(poly, session).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(changes, session: session);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ",
                result.Analysis.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.Message)));
        return (result.Root!, result.Analysis, session);
    }
}