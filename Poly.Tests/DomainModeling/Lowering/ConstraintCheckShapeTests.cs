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
            "if (assignValue0 != null && !System.Text.RegularExpressions.Regex.IsMatch(assignValue0, \"^[A-Z]{3}$\"))");
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
            "if (tag != null && !System.Text.RegularExpressions.Regex.IsMatch(tag, \"^[A-Z]+$\"))");
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

    private static string PrintCreate(Domain domain, AnalysisResult analysis, string entityName) {
        var cs = new CSharpGenerator().Generate(new DomainToCSharpExporter().Export(domain, analysis));
        return ExportedCSharp.ExtractMethod(cs, $"public static DomainResult<{entityName}> Create(");
    }

    private static string PrintAssign(Domain domain, string entityName, string propertyName, string valueName) {
        var entity = domain.Types.OfType<Entity>().First(e => e.Name == entityName);
        var pass = new EffectLoweringPass(entity, new LoweringContext(
            new ThisReference(),
            ActionResultType: new NamedTypeReference("DomainResult")));
        var lowered = pass.TryLowerVmNode(new AssignEffect(
            DomainExpression.Property(propertyName),
            DomainExpression.Property(valueName)));
        return new CSharpGenerator().Generate(lowered!);
    }
}