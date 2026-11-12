using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.Interpretation.CSharp;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// S3 characterization: pins the byte-visible shapes of the shared constraint
/// guards the create factory and assign composer print today, so the
/// ConstraintCheckWriter swap keeps goldens byte-identical.
/// </summary>
public class ConstraintCheckShapeTests {
    [Test]
    public async Task Assign_RequiredText_UsesReflectionStatic() {
        var (domain, _) = EvolvedDomain.FromDsl("""
            domain Lab
            Person: entity {
              Name: Text
              Rename: action (name: Text) { assign Name to name }
            }
            """);
        var cs = PrintAssign(domain, "Person", "Name", "name");

        await Assert.That(cs).Contains("if (System.String.IsNullOrEmpty(assignValue0))");
        await Assert.That(cs).Contains("\"'Name' is required.\"");
    }

    [Test]
    public async Task Create_RequiredText_UsesKeywordStatic() {
        var (domain, analysis) = EvolvedDomain.FromDsl("""
            domain Lab
            Person: entity {
              Name: Text
            }
            """);
        var create = PrintCreate(domain, analysis, "Person");

        await Assert.That(create).Contains("if (string.IsNullOrEmpty(name))");
        await Assert.That(create).Contains("\"'Name' is required.\"");
    }

    [Test]
    public async Task Assign_PatternGuard_KeepsNullConjunct() {
        var (domain, _) = EvolvedDomain.FromDsl("""
            domain Lab
            Tag: entity {
              Code: Text pattern("^[A-Z]{3}$")
              Relabel: action (code: Text) { assign Code to code }
            }
            """);
        var cs = PrintAssign(domain, "Tag", "Code", "code");

        await Assert.That(cs).Contains(
            "if (assignValue0 != null && !System.Text.RegularExpressions.Regex.IsMatch(assignValue0, \"^[A-Z]{3}\\\\z\"))");
    }

    [Test]
    public async Task Assign_OptionalLength_KeepsOuterIsNullOrEmptyGroup() {
        var (domain, _) = EvolvedDomain.FromDsl("""
            domain Lab
            Note: entity {
              Body: Text length(7, 20) optional
              Edit: action (body: Text) { assign Body to body }
            }
            """);
        var cs = PrintAssign(domain, "Note", "Body", "body");

        await Assert.That(cs).Contains("if (!System.String.IsNullOrEmpty(assignValue0))");
        await Assert.That(cs).Contains("assignValue0.Length < 7L");
        await Assert.That(cs).Contains("assignValue0.Length > 20L");
    }

    [Test]
    public async Task Assign_RequiredLength_EmitsFlatChecks() {
        var (domain, _) = EvolvedDomain.FromDsl("""
            domain Lab
            Note: entity {
              Body: Text length(7, 20)
              Edit: action (body: Text) { assign Body to body }
            }
            """);
        var cs = PrintAssign(domain, "Note", "Body", "body");

        await Assert.That(cs).Contains("if (assignValue0.Length < 7L)");
        await Assert.That(cs.Contains("if (!System.String.IsNullOrEmpty(assignValue0))")).IsFalse();
    }

    [Test]
    public async Task Create_OptionalLengthAndPattern_KeepPerCheckPresence() {
        var (domain, analysis) = EvolvedDomain.FromDsl("""
            domain Lab
            Widget: entity {
              Code: Text length(3, 10) optional
              Tag: Text pattern("^[A-Z]+$") optional
            }
            """);
        var create = PrintCreate(domain, analysis, "Widget");

        await Assert.That(create).Contains("if (code != null && code.Length < 3L)");
        await Assert.That(create).Contains(
            "if (tag != null && !System.Text.RegularExpressions.Regex.IsMatch(tag, \"^[A-Z]+\\\\z\"))");
    }

    [Test]
    public async Task CreateAndAssign_SameMessageForSharedConstraints() {
        var (domain, analysis) = EvolvedDomain.FromDsl("""
            domain Lab
            Widget: entity {
              Score: Number range(1, 10)
              SetScore: action (n: Number) { assign Score to n }
            }
            """);
        var create = PrintCreate(domain, analysis, "Widget");
        var assign = PrintAssign(domain, "Widget", "Score", "n");

        await Assert.That(create).Contains("\"'Score' must be >= 1.\"");
        await Assert.That(assign).Contains("\"'Score' must be >= 1.\"");
    }

    [Test]
    public async Task Pattern_WhenValueHasTrailingNewline_RejectedInSimAndCSharp() {
        var (domain, analysis) = EvolvedDomain.FromDsl("""
            domain Lab
            E: entity {
              S: Text pattern("^[A-Z]+$")
            }
            """);

        // .NET '$' also matches before a final newline, so the author's anchored
        // pattern accepted "ABC\n". The simulator and the printed factory share
        // one lowered guard, so both must reject it with the one existing message.
        var printedCs = new CSharpGenerator().Generate(DomainProgramProjection.ToSyntax(domain, analysis));

        var sim = ModuleExec.For(domain).Create(
            "E", new Dictionary<string, object?> { ["S"] = "ABC\n" });
        await Assert.That(sim.Succeeded).IsFalse();
        await Assert.That(sim.Error).IsEqualTo("'S' does not match the required pattern.");

        var asm = ExportedCSharp.CompileAndLoad(printedCs);
        var printed = ExportedCSharp.InvokeCreate(asm.GetType("E")!, ("s", "ABC\n"));
        var printedError = printed.GetType().GetProperty("ErrorMessage")!.GetValue(printed) as string;
        await Assert.That(printedError).IsEqualTo(sim.Error);
    }

    [Test]
    public async Task Assign_ConstantAnchoredPatternWithFinalNewline_KeepsGuard() {
        // L-20b: the guard-omission fold must judge a constant RHS by the same
        // absolute-end meaning the emitted guard uses. "ABC\n" is outside
        // "^[A-Z]{3}$" once '$' means \z, so the fold may not drop the check.
        var (domain, _) = EvolvedDomain.FromDsl("""
            domain Lab
            Tag: entity {
              Code: Text pattern("^[A-Z]{3}$")
              Relabel: action (code: Text) { assign Code to code }
            }
            """);
        var cs = PrintAssignLiteral(domain, "Tag", "Code", "ABC\n");

        await Assert.That(cs).Contains("System.Text.RegularExpressions.Regex.IsMatch");
        await Assert.That(cs).Contains("^[A-Z]{3}\\\\z");
    }

    [Test]
    public async Task PrintedPatternGuard_CarriesAbsoluteEndAnchor() {
        // Pass-level oracle, not a behaviour test: L-20 already prints '\z'. This pins
        // that one end-anchor semantic (L-20b's seam) is what the guard keeps.
        var (domain, analysis) = EvolvedDomain.FromDsl("""
            domain Lab
            E: entity { S: Text pattern("^[A-Z]+$") }
            """);
        var create = PrintCreate(domain, analysis, "E");

        await Assert.That(create).Contains("^[A-Z]+\\\\z");
    }

    [Test]
    public async Task Create_PatternShapes_AnchorAtAbsoluteEndOnlyWhenAuthored() {
        var (anchored, anchoredAnalysis) = EvolvedDomain.FromDsl("""
            domain Lab
            Anchored: entity { S: Text pattern("^[A-Z]+$") }
            """);
        var anchoredCreate = PrintCreate(anchored, anchoredAnalysis, "Anchored");
        await Assert.That(anchoredCreate).Contains("^[A-Z]+\\\\z");
        await Assert.That(anchoredCreate).DoesNotContain("^[A-Z]+$");

        var (unanchored, unanchoredAnalysis) = EvolvedDomain.FromDsl("""
            domain Lab
            Unanchored: entity { S: Text pattern("^[A-Z]+") }
            """);
        var unanchoredCreate = PrintCreate(unanchored, unanchoredAnalysis, "Unanchored");
        await Assert.That(unanchoredCreate).Contains("\"^[A-Z]+\"");
        await Assert.That(unanchoredCreate).DoesNotContain("\\\\z");

        var (escaped, escapedAnalysis) = EvolvedDomain.FromDsl("""
            domain Lab
            Escaped: entity { S: Text pattern("^[A-Z]+\\$") }
            """);
        var escapedCreate = PrintCreate(escaped, escapedAnalysis, "Escaped");
        await Assert.That(escapedCreate).Contains("\"^[A-Z]+\\\\$\"");
    }

    private static string PrintCreate(Domain domain, AnalysisResult analysis, string entityName) {
        var cs = new CSharpGenerator().Generate(DomainProgramProjection.ToSyntax(domain, analysis));
        return ExportedCSharp.ExtractMethod(cs, $"internal static DomainResult<{entityName}> Create(");
    }

    private static string PrintAssign(Domain domain, string entityName, string propertyName, string valueName) {
        var entity = domain.Types.OfType<Entity>().First(e => e.Name == entityName);
        var pass = new EffectLoweringPass(entity, LoweringFixtures.Context(new ThisReference(), entity) with { ActionResultType = new NamedTypeReference("DomainResult") });
        var lowered = pass.TryLowerVmNode(new AssignEffect(
            DomainExpression.Property(propertyName),
            DomainExpression.Property(valueName)));
        return new CSharpGenerator().Generate(lowered!);
    }

    private static string PrintAssignLiteral(Domain domain, string entityName, string propertyName, object? value) {
        var entity = domain.Types.OfType<Entity>().First(e => e.Name == entityName);
        var pass = new EffectLoweringPass(entity, LoweringFixtures.Context(new ThisReference(), entity) with { ActionResultType = new NamedTypeReference("DomainResult") });
        var lowered = pass.TryLowerVmNode(new AssignEffect(
            DomainExpression.Property(propertyName),
            DomainExpression.Literal(value)));
        return new CSharpGenerator().Generate(lowered!);
    }
}