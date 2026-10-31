using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;

namespace Poly.Tests.DomainModeling;

/// <summary>
/// F26: <c>Now - 60 Minutes</c> lowers to <c>DateTime.UtcNow.AddMinutes(-60)</c>, whose
/// <c>AddMinutes(double)</c> overload does not accept the long offset. The invoke is then
/// unresolved, and the comparison must still be a value comparison rather than a ring-handle
/// comparison.
/// </summary>
public class PolicyNegativeOffsetTests {
    private static (Domain Domain, AnalysisResult Analysis) Evolve() {
        var session = DomainSession.ForExtensions([ExtensionCatalog.TemporalId]);
        var changes = new PolyDslParser("""
            domain Clock
            uses temporal
            Event: entity {
              SeenAt: DateTime
              Recent: policy { SeenAt > Now - 60 Minutes }
              Ahead: policy { SeenAt > Now + 60 Minutes }
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

    private static (DomainEntityInstance Instance, Policy Recent, Policy Ahead) Clock(DateTime seenAt) {
        var (domain, _) = Evolve();
        var entity = domain.Types.OfType<Entity>().First(e => e.Name == "Event");
        var instance = DomainEntityInstance.Create(
            entity,
            new Dictionary<string, object?> { ["SeenAt"] = seenAt },
            domain: domain);
        return (instance,
            entity.Policies.First(p => p.Name == "Recent"),
            entity.Policies.First(p => p.Name == "Ahead"));
    }

    [Test]
    public async Task Recent_UtcNow_IsTrue() {
        var (instance, recent, _) = Clock(DateTime.UtcNow);

        await Assert.That(instance.EvaluatePolicy(recent)).IsTrue();
    }

    [Test]
    public async Task Recent_TwoHoursAgo_IsFalse() {
        var (instance, recent, _) = Clock(DateTime.UtcNow.AddHours(-2));

        await Assert.That(instance.EvaluatePolicy(recent)).IsFalse();
    }

    [Test]
    public async Task Ahead_UtcNow_IsFalse() {
        var (instance, _, ahead) = Clock(DateTime.UtcNow);

        await Assert.That(instance.EvaluatePolicy(ahead)).IsFalse();
    }
}