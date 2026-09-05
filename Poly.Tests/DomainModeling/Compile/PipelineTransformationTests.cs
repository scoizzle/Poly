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
