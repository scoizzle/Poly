using Microsoft.Extensions.Logging.Abstractions;

using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Constraints;
using Poly.Mcp.Surface;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// #513 / V-13: a <c>pattern(...)</c> body .NET cannot parse matches nothing, so every
/// write of the property fails at runtime with a raw <see cref="System.Text.RegularExpressions.RegexParseException"/>.
/// The authoring path must reject it instead: one positioned <c>DMSEM004</c> per distinct
/// body, decided by <see cref="PatternShape.IsValid"/> (the one <c>new Regex</c> site),
/// with the joint literal check not misattributing a <c>DMCS001</c> to the same defect.
/// </summary>
public class PatternDefinitionTests {
    private const string Invalid = "([";

    private static EvolutionResult Parse(string dsl) {
        var changes = new PolyDslParser(dsl).Parse();
        return new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(changes);
    }

    private static List<Poly.Analysis.Diagnostic> Errors(EvolutionResult r) => r.Analysis.Diagnostics
        .Where(d => d.Severity == DiagnosticSeverity.Error)
        .ToList();

    private static List<Poly.Analysis.Diagnostic> RegexErrors(EvolutionResult r) => Errors(r)
        .Where(d => d.Code == DomainModelDiagnosticCodes.SemanticConstraintMismatch)
        .ToList();

    [Test]
    public async Task InvalidRegexPattern_AtDefine_IsError() {
        var r = Parse($$"""
            domain Test
            Z: entity {
              Code: Text pattern("{{Invalid}}")
            }
            """);

        var errors = RegexErrors(r);
        await Assert.That(r.Succeeded).IsFalse();
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0].Message).Contains("is not a valid regular expression");
        await Assert.That(errors[0].Message).Contains("'(['");
        await Assert.That(errors[0].Node).IsTypeOf<Property>();
        await Assert.That(((Property)errors[0].Node).Name).IsEqualTo("Code");
    }

    /// <summary>
    /// A value member's properties are added raw (no canonical fold), so the distinct-body
    /// dedupe is what keeps an identical repeat to one defect here.
    /// </summary>
    [Test]
    public async Task InvalidRegexPattern_TwiceOnValueMember_ReportsOnce() {
        var r = Parse($$"""
            domain Test
            public value Money {
              Code: Text pattern("{{Invalid}}") pattern("{{Invalid}}")
            }
            """);

        await Assert.That(r.Succeeded).IsFalse();
        await Assert.That(RegexErrors(r).Count).IsEqualTo(1);
    }

    /// <summary>Entity properties fold an identical repeat before analysis; this pins the premise.</summary>
    [Test]
    public async Task InvalidRegexPattern_TwiceOnEntity_ReportsOnce() {
        var r = Parse($$"""
            domain Test
            Z: entity {
              Code: Text pattern("{{Invalid}}") pattern("{{Invalid}}")
            }
            """);

        await Assert.That(r.Succeeded).IsFalse();
        await Assert.That(RegexErrors(r).Count).IsEqualTo(1);
    }

    [Test]
    public async Task InvalidRegexPattern_TwoDifferentBodies_ReportsTwo() {
        var r = Parse($$"""
            domain Test
            Z: entity {
              Code: Text pattern("{{Invalid}}") pattern("[z-a]")
            }
            """);

        await Assert.That(r.Succeeded).IsFalse();
        await Assert.That(RegexErrors(r).Count).IsEqualTo(2);
    }

    [Test]
    public async Task InvalidRegexPattern_WithLength_IsOnlyTheRegexError() {
        var r = Parse($$"""
            domain Test
            Z: entity {
              Code: Text length(1, 10) pattern("{{Invalid}}")
            }
            """);

        var errors = Errors(r);
        await Assert.That(r.Succeeded).IsFalse();
        await Assert.That(RegexErrors(r).Count).IsEqualTo(1);
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors.Any(e => e.Message.Contains("unsatisfiable"))).IsFalse();
    }

    [Test]
    public async Task InvalidRegexPattern_WithLiteralDefault_IsOnlyTheRegexError() {
        var r = Parse($$"""
            domain Test
            Z: entity {
              Code: Text pattern("{{Invalid}}") default("abc")
            }
            """);

        await Assert.That(r.Succeeded).IsFalse();
        await Assert.That(RegexErrors(r).Count).IsEqualTo(1);
        await Assert.That(Errors(r).Any(e => e.Code == DomainModelDiagnosticCodes.ConstraintSatisfiability)).IsFalse();
    }

    [Test]
    public async Task InvalidRegexPattern_WithEquality_IsOnlyTheRegexError() {
        var r = Parse($$"""
            domain Test
            Z: entity {
              Code: Text pattern("{{Invalid}}") equals("abc")
            }
            """);

        await Assert.That(r.Succeeded).IsFalse();
        await Assert.That(RegexErrors(r).Count).IsEqualTo(1);
        await Assert.That(Errors(r).Any(e => e.Code == DomainModelDiagnosticCodes.ConstraintSatisfiability)).IsFalse();
    }

    /// <summary>Two distinct defects: the pattern does not apply to a Number, and it does not compile.</summary>
    [Test]
    public async Task InvalidRegexPattern_OnNonTextProperty_ReportsBothErrors() {
        var r = Parse($$"""
            domain Test
            Z: entity {
              Qty: Number pattern("{{Invalid}}")
            }
            """);

        var errors = Errors(r);
        await Assert.That(r.Succeeded).IsFalse();
        await Assert.That(errors.Any(e => e.Code == DomainModelDiagnosticCodes.SemanticTypeCompatibility)).IsTrue();
        await Assert.That(errors.Count(e => e.Code == DomainModelDiagnosticCodes.SemanticConstraintMismatch)).IsEqualTo(1);
    }

    [Test]
    public async Task InvalidRegexPattern_OnPublicValueMember_IsDmsem004Error() {
        var r = Parse($$"""
            domain Test
            public value Money {
              Code: Text pattern("{{Invalid}}")
            }

            Z: entity {
              Id: Text
            }
            """);

        var errors = RegexErrors(r);
        await Assert.That(r.Succeeded).IsFalse();
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0].Node).IsTypeOf<Property>();
        await Assert.That(((Property)errors[0].Node).Name).IsEqualTo("Code");
    }

    /// <summary>The verbatim #513 repro: <c>poly_define</c> must reject it and change nothing.</summary>
    [Test]
    public async Task Define_InvalidRegexPattern_IsRejectedWithDmsem004() {
        var surface = new PolySurfaceTools(McpTestTools.Store, NullLogger<PolySurfaceTools>.Instance);
        var workspace = surface.Workspace(name: "PatternDef").State!.Workspace;

        var accepted = surface.Define(workspace, poly: """
            domain Probe
            uses storage

            Z: entity {
              Code: Text pattern("^[A-Z]{3}$")
            }
            """);
        await Assert.That(accepted.Ok).IsTrue();
        var revision = surface.Inspect(workspace).State!.Revision;

        var response = surface.Define(workspace, poly: """
            domain Probe
            uses storage

            Z: entity {
              Code: Text pattern("([")
            }
            """);

        await Assert.That(response.Ok).IsFalse();
        var finding = response.Findings.Single(f => f.Code == "DMSEM004");
        await Assert.That(finding.Message).Contains("'(['");
        await Assert.That(finding.Path).Contains("Code");
        await Assert.That(response.State!.Revision).IsEqualTo(revision);
    }

    [Test]
    public async Task ValidRegex_IsAccepted() {
        var r = Parse("""
            domain Test
            Z: entity {
              Code: Text pattern("^[A-Z]{3}$")
            }
            """);

        await Assert.That(r.Succeeded).IsTrue();
        await Assert.That(RegexErrors(r).Count).IsEqualTo(0);
    }

    /// <summary>
    /// Valid but outside the scanner's model (lookahead, Unicode class): the envelope bails,
    /// so the gate must be <see cref="PatternShape.IsValid"/>, never <c>MaxLength is null</c>.
    /// </summary>
    [Test]
    public async Task UnmodelledButValidPattern_IsAccepted() {
        var r = Parse("""
            domain Test
            Z: entity {
              Look: Text pattern("(?=a)a")
              Letter: Text pattern("\p{L}+")
            }
            """);

        await Assert.That(r.Succeeded).IsTrue();
        await Assert.That(RegexErrors(r).Count).IsEqualTo(0);
    }

    [Test]
    public async Task EmptyPattern_IsAccepted() {
        var r = Parse("""
            domain Test
            Z: entity {
              Code: Text pattern("")
            }
            """);

        await Assert.That(r.Succeeded).IsTrue();
        await Assert.That(RegexErrors(r).Count).IsEqualTo(0);
    }

    [Test]
    public async Task PatternEvaluate_InvalidBody_IsViolatedNotThrown() {
        await Assert.That(ConstraintValidation.Evaluate(new PatternConstraint(Invalid), "abc"))
            .IsEqualTo(ConstraintVerdict.Violated);
    }
}