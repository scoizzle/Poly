using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;

namespace Poly.Tests.DomainModeling;

/// <summary>
/// F31: a DateTime <c>Now + 30 Minutes</c> in a <c>create in</c> initializer must
/// compile and execute on the VM, not die with a scalar-vs-reference rejection.
/// </summary>
public class CreateInitializerDateTimeOffsetTests {
    private static (Domain Domain, AnalysisResult Analysis) Evolve() {
        var session = DomainSession.ForExtensions([ExtensionCatalog.TemporalId]);
        var changes = new PolyDslParser("""
            domain Login
            uses temporal
            Session: entity {
              Token: Text
              ExpiresAt: DateTime
            }
            User: entity {
              sessions: many Session
              Login: action { create in sessions { Token: "t" ExpiresAt: Now + 30 Minutes } }
            }
            """, session).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(changes, session: session);
        if (!result.Succeeded) {
            var errors = string.Join("; ", result.Analysis.Diagnostics
                .Where(d => d.Severity == Poly.Analysis.DiagnosticSeverity.Error)
                .Select(d => d.Message));
            throw new InvalidOperationException($"Evolution failed: {errors}");
        }
        var analysis = DomainModelAnalyzer.Analyze(result.Root!);
        if (analysis.HasErrors) {
            var errors = string.Join("; ", analysis.Diagnostics
                .Where(d => d.Severity == Poly.Analysis.DiagnosticSeverity.Error)
                .Select(d => d.Message));
            throw new InvalidOperationException($"Analysis failed: {errors}");
        }
        return (result.Root!, analysis);
    }

    [Test]
    public async Task Login_ExpiresAtOffset_Succeeds() {
        var (domain, _) = Evolve();
        var exec = ModuleExec.For(domain);
        var user = domain.Types.OfType<Entity>().Single(e => e.Name == "User");
        var instance = exec.Create((user).Name, new Dictionary<string, object?>()).Instance!;

        var before = DateTime.UtcNow;
        var result = exec.Invoke(instance, "Login");
        var after = DateTime.UtcNow;

        await Assert.That(result.Succeeded).IsTrue();
        var session = result.CreatedChildren.Single();
        await Assert.That(session.EntityName).IsEqualTo("Session");
        await Assert.That((string)session.Bag["Token"]!).IsEqualTo("t");
        await Assert.That(session.Bag["ExpiresAt"]).IsTypeOf<DateTime>();
        var expiresAt = (DateTime)session.Bag["ExpiresAt"]!;
        await Assert.That(expiresAt).IsGreaterThanOrEqualTo(before.AddMinutes(30));
        await Assert.That(expiresAt).IsLessThanOrEqualTo(after.AddMinutes(30));
    }
}