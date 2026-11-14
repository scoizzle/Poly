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
/// Item 6: required/range/length/pattern/enum lower on assign in EffectLoweringPass
/// (same tree for simulate + C# print). A property without <c>optional</c> rejects
/// empty text; an <c>optional</c> length skips empty.
/// </summary>
public class ConstraintAssignLoweringTests {
    [Test]
    public async Task RangeAssign_Runtime_LowersToFailureGuardThenAssign() {
        var score = new Property("Score", new DomainTypeReference("Number"),
            [new RangeConstraint(1.0, 10.0)]);
        var entity = new Entity("Widget",
            Properties: [score],
            Actions: [], Policies: [], Stages: []);
        var pass = new EffectLoweringPass(entity, LoweringFixtures.Context(new ThisReference(), entity) with { ActionResultType = new NamedTypeReference("DomainResult") });

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
        var pass = new EffectLoweringPass(entity, LoweringFixtures.Context(new ThisReference(), entity));

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
        var exec = ModuleExec.For(domain);
        _ = analysis;
        var entity = domain.Types.OfType<Entity>().First(e => e.Name == "Widget");
        var instance = exec.Create((entity).Name, new Dictionary<string, object?> { ["Score"] = 5.0 }).Instance!;

        var result = exec.Invoke(instance, "SetScore", new Dictionary<string, object?> { ["n"] = 99.0 });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Error).Contains("must be");
        await Assert.That(Convert.ToDouble(instance.Bag["Score"])).IsEqualTo(5.0);
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
        var exec = ModuleExec.For(domain);
        var entity = domain.Types.OfType<Entity>().First(e => e.Name == "Tag");
        var instance = exec.Create((entity).Name, new Dictionary<string, object?> { ["Code"] = "ABC" }).Instance!;

        var result = exec.Invoke(instance, "Relabel", new Dictionary<string, object?> { ["code"] = "nope" });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Error)
            .Contains("does not match the required pattern");
        await Assert.That((string)instance.Bag["Code"]!).IsEqualTo("ABC");
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
        var exec = ModuleExec.For(domain);
        var entity = domain.Types.OfType<Entity>().First(e => e.Name == "Tag");
        var instance = exec.Create((entity).Name, new Dictionary<string, object?> { ["Code"] = "OK" }).Instance!;

        // Lowered tree must null-guard IsMatch (export: bare IsMatch(null) throws).
        var pass = new EffectLoweringPass(entity, LoweringFixtures.Context(new Parameter("entity", new TypeReference(entity.Name)), entity));
        var lowered = pass.TryLowerVmNode(new AssignEffect(
            DomainExpression.Property("Code"),
            DomainExpression.Property("code")));
        var cs = new CSharpGenerator().Generate(lowered!);
        await Assert.That(cs.Contains("!= null") || cs.Contains("is not null")).IsTrue();
        await Assert.That(cs).Contains("IsMatch");

        // The simulator turns a null Text action argument into "" (X-05's remainder), and since
        // L-08b an empty string is a value, so the pattern refuses it. The prior value stays.
        var result = exec.Invoke(instance, "Relabel", new Dictionary<string, object?> { ["code"] = null });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Error).Contains("'Code' does not match the required pattern.");
        await Assert.That((string)instance.Bag["Code"]!).IsEqualTo("OK");
    }

    [Test]
    public async Task Length_WithRequired_EmptyFailsLength() {
        var (domain, _) = Evolve("""
            domain Lab
            Label: entity {
              Name: Text length(7, 20)
              Rename: action (name: Text) {
                assign Name to name
              }
            }
            """);
        var exec = ModuleExec.For(domain);
        var entity = domain.Types.OfType<Entity>().First(e => e.Name == "Label");
        var instance = exec.Create((entity).Name, new Dictionary<string, object?> { ["Name"] = "abcdefg" }).Instance!;

        var result = exec.Invoke(instance, "Rename", new Dictionary<string, object?> { ["name"] = "" });

        // L-08b: the empty string is a value; the length refuses it, not required.
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Error).Contains("'Name' must be at least 7 characters.");
        await Assert.That((string)instance.Bag["Name"]!).IsEqualTo("abcdefg");
    }

    [Test]
    public async Task Length_BareProperty_EmptyFailsLength_TooShortFails() {
        var (domain, _) = Evolve("""
            domain Lab
            Note: entity {
              Body: Text length(7, 20)
              Edit: action (body: Text) {
                assign Body to body
              }
            }
            """);
        var exec = ModuleExec.For(domain);
        var entity = domain.Types.OfType<Entity>().First(e => e.Name == "Note");
        var emptyFail = exec.Create((entity).Name, new Dictionary<string, object?> { ["Body"] = "abcdefg" }).Instance!;
        var emptyResult = exec.Invoke(emptyFail, "Edit", new Dictionary<string, object?> { ["body"] = "" });
        await Assert.That(emptyResult.Succeeded).IsFalse();
        await Assert.That(emptyResult.Error).Contains("'Body' must be at least 7 characters.");
        await Assert.That((string)emptyFail.Bag["Body"]!).IsEqualTo("abcdefg");

        var shortFail = exec.Create((entity).Name, new Dictionary<string, object?> { ["Body"] = "abcdefg" }).Instance!;
        var shortResult = exec.Invoke(shortFail, "Edit", new Dictionary<string, object?> { ["body"] = "abc" });
        await Assert.That(shortResult.Succeeded).IsFalse();
        await Assert.That(shortResult.Error).Contains("at least");
        await Assert.That((string)shortFail.Bag["Body"]!).IsEqualTo("abcdefg");
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
        await Assert.That(ActionBodies.TryGetModuleBody(
            domain, "Widget", "SetScore", out var method)).IsTrue();
        await Assert.That(method?.Body).IsNotNull();
        var moduleCs = new CSharpGenerator().Generate(method!.Body!);
        await Assert.That(moduleCs).Contains("must be >=");
        await Assert.That(moduleCs).Contains("Failure");

        var entity = domain.Types.OfType<Entity>().First(e => e.Name == "Widget");
        var action = entity.Actions.First(a => a.Name == "SetScore");
        var exportPass = new EffectLoweringPass(entity, LoweringFixtures.Context(new ThisReference(), entity) with { Analysis = analysis, Domain = domain, ActionParameterNames = ["n"] });
        var exportLowered = exportPass.LowerActionBody(action.Effects);
        var exportCs = new CSharpGenerator().Generate(exportLowered!);
        await Assert.That(exportCs).Contains("must be >=");
        await Assert.That(ReferenceEquals(moduleCs, exportCs)
            || (moduleCs.Contains("must be >=") && exportCs.Contains("must be >="))).IsTrue();
    }

    [Test]
    public async Task UniqueAssign_StillLowersEnsureUnique_Regression() {
        var entity = PermitEntity();
        var pass = new EffectLoweringPass(entity, LoweringFixtures.Context(new Parameter("entity", new TypeReference(entity.Name)), entity));

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
        var domain = ValidDomain.Create("Parking", [entity]);
        var exec = ModuleExec.For(domain);
        var instance = exec.Create((entity).Name, new Dictionary<string, object?> { ["Plate"] = "XYZ999" }).Instance!;

        var result = exec.Invoke(instance, "Relabel", new Dictionary<string, object?> { ["plate"] = "ABC123" });
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That((string)instance.Bag["Plate"]!).IsEqualTo("ABC123");
    }

    [Test]
    public async Task EnumAssign_OutOfMember_Fails() {
        var (domain, _) = Evolve("""
            domain Lab
            PatronStatus: enum { Active, Suspended }
            Patron: entity {
              Status: PatronStatus default(Active)
              SetStatus: action (status: PatronStatus) {
                assign Status to status
              }
            }
            """);
        var exec = ModuleExec.For(domain);
        var entity = domain.Types.OfType<Entity>().First(e => e.Name == "Patron");
        var instance = exec.Create((entity).Name, new Dictionary<string, object?>()).Instance!;

        var result = exec.Invoke(instance, "SetStatus", new Dictionary<string, object?> { ["status"] = "Bogus" });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Error)
            .IsEqualTo("'Status' is not a valid member of PatronStatus.");
        await Assert.That((string)instance.Bag["Status"]!).IsEqualTo("Active");
    }

    [Test]
    public async Task Create_WhenEnumOutOfMember_Fails() {
        var (domain, _) = Evolve("""
            domain Lab
            PatronStatus: enum { Active, Suspended }
            Patron: entity {
              Status: PatronStatus default(Active)
            }
            """);
        var exec = ModuleExec.For(domain);
        var entity = domain.Types.OfType<Entity>().First(e => e.Name == "Patron");

        var thrown = exec.Create(entity.Name, new Dictionary<string, object?> { ["Status"] = "Bogus" });
        await Assert.That(thrown.Succeeded).IsFalse();
        await Assert.That(thrown.Error)
            .IsEqualTo("'Bogus' is not a valid member of PatronStatus for property 'Status'.");
    }

    [Test]
    public async Task AcceptedAssign_RuntimeGuardDoesNotTrip() {
        var (domain, analysis) = Evolve("""
            domain Lab
            PatronStatus: enum { Active, Suspended }
            Widget: entity {
              Score: Number range(1, 10) default(5)
              Status: PatronStatus default(Active)
              Name: Text
              Code: Text length(2, 5)
              Tag: Text pattern("^[A-Z]+$")
              SetScore: action { assign Score to 7 }
              SetStatus: action (status: PatronStatus) { assign Status to status }
              Rename: action { assign Name to "Ada" }
              SetCode: action { assign Code to "ab" }
              SetTag: action { assign Tag to "OK" }
            }
            """);
        var exec = ModuleExec.For(domain);
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.EffectConstraintViolation &&
            d.Severity == DiagnosticSeverity.Error)).IsFalse();

        var entity = domain.Types.OfType<Entity>().First(e => e.Name == "Widget");
        var instance = exec.Create((entity).Name, new Dictionary<string, object?> {
            ["Name"] = "Ada",
            ["Code"] = "xy",
            ["Tag"] = "A"
        }).Instance!;

        var setScore = exec.Invoke(instance, "SetScore");
        await Assert.That(setScore.Succeeded).IsTrue();
        var setStatus = exec.Invoke(instance, "SetStatus", new Dictionary<string, object?> { ["status"] = "Suspended" });
        await Assert.That(setStatus.Succeeded).IsTrue();
        var rename = exec.Invoke(instance, "Rename");
        await Assert.That(rename.Succeeded).IsTrue();
        var setCode = exec.Invoke(instance, "SetCode");
        await Assert.That(setCode.Succeeded).IsTrue();
        var setTag = exec.Invoke(instance, "SetTag");
        await Assert.That(setTag.Succeeded).IsTrue();
        await Assert.That((string)instance.Bag["Status"]!).IsEqualTo("Suspended");
    }

    [Test]
    public async Task RequiredTextAssign_Empty_IsAccepted() {
        var (domain, _) = Evolve("""
            domain Lab
            Person: entity {
              Name: Text
              Rename: action (name: Text) {
                assign Name to name
              }
            }
            """);
        var exec = ModuleExec.For(domain);
        var entity = domain.Types.OfType<Entity>().First(e => e.Name == "Person");
        var instance = exec.Create((entity).Name, new Dictionary<string, object?> { ["Name"] = "Ada" }).Instance!;

        // L-08b (owner ruling 2026-11-13): an empty string is a value, so a required Text accepts it.
        var result = exec.Invoke(instance, "Rename", new Dictionary<string, object?> { ["name"] = "" });
        await Assert.That(result.Succeeded).IsTrue().Because(result.Error ?? "");
        await Assert.That((string)instance.Bag["Name"]!).IsEqualTo("");
    }

    [Test]
    public async Task BareTextAssign_Empty_IsAccepted() {
        var (domain, _) = Evolve("""
            domain Lab
            Person: entity {
              Name: Text
              Rename: action (name: Text) {
                assign Name to name
              }
            }
            """);
        var exec = ModuleExec.For(domain);
        var entity = domain.Types.OfType<Entity>().First(e => e.Name == "Person");
        var instance = exec.Create((entity).Name, new Dictionary<string, object?> { ["Name"] = "Ada" }).Instance!;

        // L-08b (owner ruling 2026-11-13): an empty string is a value, so a required Text accepts it.
        var result = exec.Invoke(instance, "Rename", new Dictionary<string, object?> { ["name"] = "" });
        await Assert.That(result.Succeeded).IsTrue().Because(result.Error ?? "");
        await Assert.That((string)instance.Bag["Name"]!).IsEqualTo("");
    }

    [Test]
    public async Task Length_Optional_EmptyFailsLength() {
        var (domain, _) = Evolve("""
            domain Lab
            Note: entity {
              Body: Text length(7, 20) optional
              Edit: action (body: Text) {
                assign Body to body
              }
            }
            """);
        var exec = ModuleExec.For(domain);
        var entity = domain.Types.OfType<Entity>().First(e => e.Name == "Note");
        var instance = exec.Create((entity).Name, new Dictionary<string, object?>()).Instance!;
        // L-08b: an empty string is a value, so an optional Text's length applies to it.
        var result = exec.Invoke(instance, "Edit", new Dictionary<string, object?> { ["body"] = "" });
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Error).Contains("'Body' must be at least 7 characters.");
        await Assert.That(instance.Bag["Body"]).IsNull();
    }

    [Test]
    public async Task EqualityAssign_OutOfValue_FailsAndLeavesPriorValue() {
        var (domain, _) = Evolve("""
            domain Lab
            Widget: entity {
              Status: Text equals("Active") optional
              SetStatus: action (status: Text) {
                assign Status to status
              }
            }
            """);
        var exec = ModuleExec.For(domain);
        var entity = domain.Types.OfType<Entity>().First(e => e.Name == "Widget");
        var instance = exec.Create((entity).Name, new Dictionary<string, object?> { ["Status"] = "Active" }).Instance!;

        var result = exec.Invoke(instance, "SetStatus", new Dictionary<string, object?> { ["status"] = "Inactive" });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Error).Contains("must equal");
        await Assert.That((string)instance.Bag["Status"]!).IsEqualTo("Active");
    }

    [Test]
    public async Task EqualsAssign_ModuleBody_ContainsMustEqualGuard() {
        var (domain, analysis, session) = EvolveWithSession("""
            domain Lab
            Widget: entity {
              Status: Text equals("Active") optional
              SetStatus: action (status: Text) {
                assign Status to status
              }
            }
            """);
        session.Lower(domain, analysis);
        await Assert.That(ActionBodies.TryGetModuleBody(
            domain, "Widget", "SetStatus", out var method)).IsTrue();
        await Assert.That(method?.Body).IsNotNull();
        var moduleCs = new CSharpGenerator().Generate(method!.Body!);
        await Assert.That(moduleCs).Contains("must equal");
        await Assert.That(moduleCs).Contains("Failure");
    }

    [Test]
    public async Task EqualityAssign_NullOnOptional_FailsAndLeavesPriorValue() {
        var (domain, _) = Evolve("""
            domain Lab
            Widget: entity {
              Status: Text equals("Active") optional
              SetStatus: action (status: Text) {
                assign Status to status
              }
            }
            """);
        var exec = ModuleExec.For(domain);
        var entity = domain.Types.OfType<Entity>().First(e => e.Name == "Widget");
        var instance = exec.Create((entity).Name, new Dictionary<string, object?> { ["Status"] = "Active" }).Instance!;

        var result = exec.Invoke(instance, "SetStatus", new Dictionary<string, object?> { ["status"] = null });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Error).Contains("must equal");
        await Assert.That((string)instance.Bag["Status"]!).IsEqualTo("Active");
    }

    [Test]
    public async Task EqualityAssign_InValue_SucceedsAndWrites() {
        var (domain, _) = Evolve("""
            domain Lab
            Widget: entity {
              Status: Text equals("Active") optional
              SetStatus: action (status: Text) { assign Status to status }
            }
            """);
        var exec = ModuleExec.For(domain);
        var entity = domain.Types.OfType<Entity>().First(e => e.Name == "Widget");
        var instance = exec.Create((entity).Name, new Dictionary<string, object?> { ["Status"] = "Active" }).Instance!;

        var result = exec.Invoke(instance, "SetStatus", new Dictionary<string, object?> { ["status"] = "Active" });

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That((string)instance.Bag["Status"]!).IsEqualTo("Active");
    }

    [Test]
    public async Task AssignPatternBeforeLength_ReportsCategoryOrderFirst() {
        var (domain, _) = Evolve("""
            domain Lab
            Tag: entity {
              Code: Text pattern("^[A-Z]{3}$") length(3, 10)
              Relabel: action (code: Text) { assign Code to code }
            }
            """);
        var exec = ModuleExec.For(domain);
        var entity = domain.Types.OfType<Entity>().First(e => e.Name == "Tag");
        var instance = exec.Create((entity).Name, new Dictionary<string, object?> { ["Code"] = "ABC" }).Instance!;

        var result = exec.Invoke(instance, "Relabel", new Dictionary<string, object?> { ["code"] = "ab" });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Error).Contains("must be at least 3");
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
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(changes);
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
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(changes, session: session);
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