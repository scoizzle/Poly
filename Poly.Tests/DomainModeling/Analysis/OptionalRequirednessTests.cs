using Poly.Analysis;
using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// Requiredness is one published fact: every entity property is required at create
/// unless it carries <c>optional</c>. <c>default(...)</c> and the first stage's entry
/// assignment still supply a value, so they are not missing-create errors.
/// </summary>
public class OptionalRequirednessTests {
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

    /// <summary>Evolves a domain that is EXPECTED to fail analysis; returns the
    /// concatenated error messages.</summary>
    private static string EvolveExpectingError(string poly) {
        var changes = new PolyDslParser(poly).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(changes);
        if (result.Succeeded)
            throw new InvalidOperationException("Expected evolution to fail, but it succeeded.");
        return string.Join("; ",
            result.Analysis.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.Message));
    }

    [Test]
    public async Task Create_OmittingBareProperty_ReportsDMEFF011() {
        // A property with no `optional` is required: a create that omits it must
        // fail analysis, not only at runtime.
        var message = EvolveExpectingError("""
            domain CreateBare
            Token: entity { Lexeme: Text }
            Box: entity {
              tokens: many Token
              Make: action -> Token {
                create in tokens { }
              }
            }
            """);
        await Assert.That(message).Contains("required property 'Lexeme'");
    }

    [Test]
    public async Task Parse_RequiredWord_IsRejected() {
        var message = Assert.Throws<FormatException>(() => Evolve("""
            domain Both
            Token: entity { Lexeme: Text required optional }
            """));
        await Assert.That(message!.Message).Contains("'required' is not a constraint");
    }

    [Test]
    public async Task Analyze_RequiredWithDefault_MayBeOmitted() {
        var (domain, analysis) = Evolve("""
            domain CreateDefault
            Token: entity { Lexeme: Text default("let") }
            Box: entity {
              tokens: many Token
              Make: action -> Token {
                create in tokens { }
              }
            }
            """);
        var token = domain.Types.OfType<Entity>().Single(e => e.Name == "Token");
        await Assert.That(analysis.GetStructure(token)!.RequiredPropertyNames.Contains("Lexeme")).IsTrue();
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Severity == DiagnosticSeverity.Error
            && d.Message.Contains("required property", StringComparison.Ordinal))).IsFalse();
    }

    [Test]
    public async Task Analyze_EntryAssignedProperty_MayBeOmitted() {
        var (domain, analysis) = Evolve("""
            domain CreateEntry
            Token: entity {
              Lexeme: Text
              Frozen: Boolean
              Active: stage {
                entry { assign Frozen to true }
              }
            }
            Box: entity {
              tokens: many Token
              Make: action -> Token {
                create in tokens { Lexeme: "let" }
              }
            }
            """);
        var token = domain.Types.OfType<Entity>().Single(e => e.Name == "Token");
        await Assert.That(analysis.GetStructure(token)!.RequiredPropertyNames.Contains("Frozen")).IsTrue();
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Severity == DiagnosticSeverity.Error
            && d.Message.Contains("required property", StringComparison.Ordinal))).IsFalse();
    }

    [Test]
    public async Task Analyze_OptionalProperty_IsNotRequired() {
        var (domain, analysis) = Evolve("""
            domain OptionalFact
            Token: entity { Lexeme: Text optional }
            """);
        var token = domain.Types.OfType<Entity>().Single();
        await Assert.That(analysis.GetStructure(token)!.RequiredPropertyNames.Contains("Lexeme")).IsFalse();
    }

    [Test]
    public async Task ActionParameter_Bare_IsRequired() {
        var (domain, _) = Evolve("""
            domain Params
            Token: entity {
              Lexeme: Text
              Rename: action (name: Text) { assign Lexeme to name }
            }
            """);
        var behavior = BehaviorMetadata.BuildBehavior(domain);
        var rename = behavior.Entities.Single(e => e.Name == "Token").Actions.Single(a => a.Name == "Rename");
        await Assert.That(rename.Parameters.Single().IsRequired).IsTrue();
    }
}