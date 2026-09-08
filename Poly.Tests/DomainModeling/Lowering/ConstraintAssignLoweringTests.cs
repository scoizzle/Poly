using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;
using Poly.Interpretation.CSharp;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// Item 6: required/range/length/pattern lower on assign in EffectLoweringPass
/// (same tree for simulate + C# print). Optional length skips empty without required.
/// </summary>
public class ConstraintAssignLoweringTests {
    [Test]
    public async Task RangeAssign_Runtime_LowersToFailureGuardThenAssign() {
        var score = new Property("Score", new DomainTypeReference("Number"),
            [new RangeConstraint(1.0, 10.0)]);
        var entity = new Entity("Widget",
            Properties: [score],
            Actions: [], Policies: [], Stages: []);
        var pass = new EffectLoweringPass(entity, new LoweringContext(
            new Parameter("entity", new TypeReference(entity.Name))));

        var lowered = pass.TryLowerVmNode(new AssignEffect(
            DomainExpression.Property("Score"),
            DomainExpression.Property("n")));

        await Assert.That(lowered).IsNotNull();
        var cs = new CSharpGenerator().Generate(lowered!);
        await Assert.That(cs.Contains("must be >=") || cs.Contains("Failure")).IsTrue();
        var flat = Flatten(lowered!).ToList();
        await Assert.That(flat.Any(n => n is IfStatement)).IsTrue();
        await Assert.That(flat.Any(n =>
            n is Assignment { Destination: Member { MemberName: "Score" } })).IsTrue();
        var failIdx = flat.FindIndex(n =>
            n is Return ret
            && new CSharpGenerator().Generate(ret).Contains("Failure"));
        var assignIdx = flat.FindIndex(n =>
            n is Assignment { Destination: Member { MemberName: "Score" } });
        await Assert.That(failIdx).IsGreaterThanOrEqualTo(0);
        await Assert.That(assignIdx).IsGreaterThan(failIdx);
    }

    [Test]
    public async Task RangeAssign_Export_ContainsMustBeOrFailure() {
        var score = new Property("Score", new DomainTypeReference("Number"),
            [new RangeConstraint(1.0, 10.0)]);
        var entity = new Entity("Widget",
            Properties: [score],
            Actions: [], Policies: [], Stages: []);
        var pass = new EffectLoweringPass(entity, new LoweringContext(
            new ThisReference(),
            UseThisReference: true));

        var lowered = pass.TryLowerVmNode(new AssignEffect(
            DomainExpression.Property("Score"),
            DomainExpression.Property("n")));
        var cs = new CSharpGenerator().Generate(lowered!);

        await Assert.That(cs.Contains("must be >=") || cs.Contains("Failure")).IsTrue();
        await Assert.That(cs).Contains("Score");
    }

    [Test]
    public async Task InvokeAction_RangeOutOfBounds_FailsAndLeavesPriorValue() {
        var (domain, analysis) = Evolve("""
            domain Lab
            Widget: entity {
              Score: Number range(1, 10)
              SetScore: action (n: Number) {
                assign Score to n
              }
            }
            """);
        _ = analysis;
        var entity = domain.Types.OfType<Entity>().First(e => e.Name == "Widget");
        var instance = DomainEntityInstance.Create(entity,
            new Dictionary<string, object?> { ["Score"] = 5.0 },
            domain: domain);

        var result = instance.InvokeAction("SetScore",
            new Dictionary<string, object?> { ["n"] = 99.0 });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.ErrorMessage).Contains("must be");
        await Assert.That(instance.GetProperty<double>("Score")).IsEqualTo(5.0);
    }

    [Test]
    public async Task PatternAssign_Mismatch_IsFailure() {
        var (domain, _) = Evolve("""
            domain Lab
            Tag: entity {
              Code: Text pattern("^[A-Z]{3}$")
              Relabel: action (code: Text) {
                assign Code to code
              }
            }
            """);
        var entity = domain.Types.OfType<Entity>().First(e => e.Name == "Tag");
        var instance = DomainEntityInstance.Create(entity,
            new Dictionary<string, object?> { ["Code"] = "ABC" },
            domain: domain);

        var result = instance.InvokeAction("Relabel",
            new Dictionary<string, object?> { ["code"] = "nope" });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.ErrorMessage)
            .Contains("does not match the required pattern");
        await Assert.That(instance.GetProperty<string>("Code")).IsEqualTo("ABC");
    }

    [Test]
    public async Task PatternAssign_Null_DoesNotThrow_RetainsPriorValue() {
        var (domain, _) = Evolve("""
            domain Lab
            Tag: entity {
              Code: Text pattern("^[A-Z]+$")
              Relabel: action (code: Text) {
                assign Code to code
              }
            }
            """);
        var entity = domain.Types.OfType<Entity>().First(e => e.Name == "Tag");
        var instance = DomainEntityInstance.Create(entity,
            new Dictionary<string, object?> { ["Code"] = "OK" },
            domain: domain);

        // Lowered tree must null-guard IsMatch (export: bare IsMatch(null) throws).
        var pass = new EffectLoweringPass(entity, new LoweringContext(
            new Parameter("entity", new TypeReference(entity.Name))));
        var lowered = pass.TryLowerVmNode(new AssignEffect(
            DomainExpression.Property("Code"),
            DomainExpression.Property("code")));
        var cs = new CSharpGenerator().Generate(lowered!);
        await Assert.That(cs.Contains("!= null") || cs.Contains("is not null")).IsTrue();
        await Assert.That(cs).Contains("IsMatch");

        // Bag Text reads coerce null→"" so runtime Failure (prior retained);
        // never ArgumentNullException from Regex.IsMatch.
        var result = instance.InvokeAction("Relabel",
            new Dictionary<string, object?> { ["code"] = null });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.ErrorMessage)
            .Contains("does not match the required pattern");
        await Assert.That(instance.GetProperty<string>("Code")).IsEqualTo("OK");
    }

    [Test]
    public async Task Length_WithRequired_EmptyFails() {
        var (domain, _) = Evolve("""
            domain Lab
            Label: entity {
              Name: Text length(7, 20) required
              Rename: action (name: Text) {
                assign Name to name
              }
            }
            """);
        var entity = domain.Types.OfType<Entity>().First(e => e.Name == "Label");
        var instance = DomainEntityInstance.Create(entity,
            new Dictionary<string, object?> { ["Name"] = "abcdefg" },
            domain: domain);

        var result = instance.InvokeAction("Rename",
            new Dictionary<string, object?> { ["name"] = "" });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.ErrorMessage).Contains("is required");
        await Assert.That(instance.GetProperty<string>("Name")).IsEqualTo("abcdefg");
    }

    [Test]
    public async Task Length_WithoutRequired_EmptySucceeds_TooShortFails() {
        var (domain, _) = Evolve("""
            domain Lab
            Note: entity {
              Body: Text length(7, 20)
              Edit: action (body: Text) {
                assign Body to body
              }
            }
            """);
        var entity = domain.Types.OfType<Entity>().First(e => e.Name == "Note");
        var emptyOk = DomainEntityInstance.Create(entity,
            new Dictionary<string, object?> { ["Body"] = "abcdefg" },
            domain: domain);
        var emptyResult = emptyOk.InvokeAction("Edit",
            new Dictionary<string, object?> { ["body"] = "" });
        await Assert.That(emptyResult.Succeeded).IsTrue();
        await Assert.That(emptyOk.GetProperty<string>("Body")).IsEqualTo("");

        var shortFail = DomainEntityInstance.Create(entity,
            new Dictionary<string, object?> { ["Body"] = "abcdefg" },
            domain: domain);
        var shortResult = shortFail.InvokeAction("Edit",
            new Dictionary<string, object?> { ["body"] = "abc" });
        await Assert.That(shortResult.Succeeded).IsFalse();
        await Assert.That(shortResult.ErrorMessage).Contains("at least");
        await Assert.That(shortFail.GetProperty<string>("Body")).IsEqualTo("abcdefg");
    }

    [Test]
    public async Task ModuleBody_AndExport_ContainFailureGuardsBeforeAssign() {
        var (domain, analysis, session) = EvolveWithSession("""
            domain Lab
            Widget: entity {
              Score: Number range(1, 10)
              SetScore: action (n: Number) {
                assign Score to n
              }
            }
            """);
        session.Lower(domain, analysis);
        await Assert.That(RuntimeAnalysisCache.TryGetModuleMethod(
            domain, "Widget", "SetScore", out var method)).IsTrue();
        await Assert.That(method?.Body).IsNotNull();
        var moduleCs = new CSharpGenerator().Generate(method!.Body!);
        await Assert.That(moduleCs).Contains("must be >=");
        await Assert.That(moduleCs).Contains("Failure");

        var entity = domain.Types.OfType<Entity>().First(e => e.Name == "Widget");
        var action = entity.Actions.First(a => a.Name == "SetScore");
        var exportPass = new EffectLoweringPass(entity, new LoweringContext(
            new ThisReference(),
            UseThisReference: true,
            Analysis: analysis,
            Domain: domain,
            ActionParameterNames: ["n"]));
        var exportLowered = exportPass.LowerActionBody(action.Effects);
        var exportCs = new CSharpGenerator().Generate(exportLowered!);
        await Assert.That(exportCs).Contains("must be >=");
        await Assert.That(ReferenceEquals(moduleCs, exportCs)
            || (moduleCs.Contains("must be >=") && exportCs.Contains("must be >="))).IsTrue();
    }

    [Test]
    public async Task UniqueAssign_StillLowersEnsureUnique_Regression() {
        var entity = PermitEntity();
        var pass = new EffectLoweringPass(entity, new LoweringContext(
            new Parameter("entity", new TypeReference(entity.Name))));

        var lowered = pass.TryLowerVmNode(new AssignEffect(
            DomainExpression.Property("Plate"),
            DomainExpression.Property("plate")));

        await Assert.That(Flatten(lowered!).Any(n =>
            n is Invoke { Delegate: Member { MemberName: "EnsureUnique" } } inv
            && inv.Arguments is [Constant { Value: "Plate" }, _])).IsTrue();
        await Assert.That(Flatten(lowered!).Any(n =>
            n is Assignment { Destination: Member { MemberName: "Plate" } })).IsTrue();
    }

    [Test]
    public async Task UniqueAssign_InvokeAction_StillWorks() {
        var entity = PermitWithRelabel();
        var domain = DomainTestFactory.Create("Parking", [entity]);
        var instance = DomainEntityInstance.Create(entity,
            new Dictionary<string, object?> { ["Plate"] = "XYZ999" }, domain: domain);

        var result = instance.InvokeAction("Relabel",
            new Dictionary<string, object?> { ["plate"] = "ABC123" });
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(instance.GetProperty<string>("Plate")).IsEqualTo("ABC123");
    }

    [Test]
    public async Task RequiredTextAssign_Empty_IsFailure() {
        var (domain, _) = Evolve("""
            domain Lab
            Person: entity {
              Name: Text required
              Rename: action (name: Text) {
                assign Name to name
              }
            }
            """);
        var entity = domain.Types.OfType<Entity>().First(e => e.Name == "Person");
        var instance = DomainEntityInstance.Create(entity,
            new Dictionary<string, object?> { ["Name"] = "Ada" },
            domain: domain);

        var result = instance.InvokeAction("Rename",
            new Dictionary<string, object?> { ["name"] = "" });
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.ErrorMessage).Contains("'Name' is required.");
        await Assert.That(instance.GetProperty<string>("Name")).IsEqualTo("Ada");
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

    private static (Domain Domain, AnalysisResult Analysis, DomainSession Session) EvolveWithSession(
        string poly) {
        var session = DomainSession.ForSource(poly, ExtensionCatalog.ProductAuthoring);
        var changes = new PolyDslParser(poly, session).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(changes, session: session);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ",
                result.Analysis.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.Message)));
        return (result.Root!, result.Analysis, session);
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
