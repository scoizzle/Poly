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
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(changes, session: session);
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
        var user = domain.Types.OfType<Entity>().Single(e => e.Name == "User");
        var instance = DomainEntityInstance.Create(user, domain: domain);

        var before = DateTime.UtcNow;
        var result = instance.InvokeAction("Login");
        var after = DateTime.UtcNow;

        await Assert.That(result.Succeeded).IsTrue();
        var session = instance.CreatedChildren.Single();
        await Assert.That(session.Entity.Name).IsEqualTo("Session");
        await Assert.That(session.GetProperty<string>("Token")).IsEqualTo("t");
        await Assert.That(session.GetProperty<object>("ExpiresAt")).IsTypeOf<DateTime>();
        var expiresAt = session.GetProperty<DateTime>("ExpiresAt");
        await Assert.That(expiresAt).IsGreaterThanOrEqualTo(before.AddMinutes(30));
        await Assert.That(expiresAt).IsLessThanOrEqualTo(after.AddMinutes(30));
    }
}