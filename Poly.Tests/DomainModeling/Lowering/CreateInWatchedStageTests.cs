using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;
using Poly.Interpretation.CSharp;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// F28: <c>create in jobs</c> lands the child in its first stage. When a
/// subscription watches that first stage, the generated <c>CreateJobs</c> body
/// must notify once so the subscriber runs on creation.
/// </summary>
public class CreateInWatchedStageTests {
    private const string Positive = """
        domain Work
        User: entity {
          OpenCount: Number default(0)
          jobs: many Job
          when jobs Open { assign OpenCount to OpenCount + 1 }
          Start: action { create in jobs { Title: "a" } }
        }
        Job: entity {
          Title: Text required
          Open: stage { }
        }
        """;

    private const string Negative = """
        domain Work
        User: entity {
          OpenCount: Number default(0)
          jobs: many Job
          when jobs Open { assign OpenCount to OpenCount + 1 }
          Start: action { create in jobs { Title: "a" } }
        }
        Job: entity {
          Title: Text required
          New: stage { }
          Open: stage { }
        }
        """;

    [Test]
    public async Task Create_IntoWatchedFirstStage_NotifiesOnce() {
        var (domain, analysis) = Evolve(Positive);
        var store = new DomainInstanceStore();
        var user = CreateUser(domain);
        store.Add(user);

        await Assert.That(user.InvokeAction("Start").Succeeded).IsTrue();
        await Assert.That(user.GetProperty<object>("OpenCount")).IsEqualTo(1L);

        var cs = new CSharpGenerator().Generate(new DomainToCSharpExporter().Export(domain, analysis));
        var createJobs = ExportedCSharp.ExtractMethod(cs, "DomainResult<Job> CreateJobs(");
        await Assert.That(CountOccurrences(createJobs, "job.NotifyOpenSubscribers(null)")).IsEqualTo(1);
    }

    [Test]
    public async Task Create_IntoUnwatchedFirstStage_StaysZero() {
        var (domain, analysis) = Evolve(Negative);
        var store = new DomainInstanceStore();
        var user = CreateUser(domain);
        store.Add(user);

        await Assert.That(user.InvokeAction("Start").Succeeded).IsTrue();
        await Assert.That(user.GetProperty<object>("OpenCount")).IsEqualTo(0L);

        var cs = new CSharpGenerator().Generate(new DomainToCSharpExporter().Export(domain, analysis));
        var createJobs = ExportedCSharp.ExtractMethod(cs, "DomainResult<Job> CreateJobs(");
        await Assert.That(CountOccurrences(createJobs, "job.NotifyOpenSubscribers(null)")).IsEqualTo(0);
    }

    [Test]
    public async Task Create_InWatchedStage_NotifyParameterIsNullable() {
        // A create-in has no previous stage, so the printed call passes null. The
        // parameter must be nullable or the generated C# fails to compile (CS1503).
        var (domain, analysis) = Evolve(Positive);
        var cs = new CSharpGenerator().Generate(new DomainToCSharpExporter().Export(domain, analysis));
        await Assert.That(cs).Contains("NotifyOpenSubscribers(JobStage? previousStage)");
    }

    private static DomainEntityInstance CreateUser(Domain domain) {
        var userEntity = domain.Types.OfType<Entity>().First(e => e.Name == "User");
        return DomainEntityInstance.Create(userEntity, domain: domain);
    }

    private static int CountOccurrences(string text, string value) =>
        text.Split(value, StringSplitOptions.None).Length - 1;

    private static (Domain Domain, AnalysisResult Analysis) Evolve(string poly) {
        var changes = new PolyDslParser(poly).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(changes);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ",
                result.Analysis.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.Message)));
        var analysis = DomainModelAnalyzer.Analyze(result.Root!);
        if (analysis.HasErrors)
            throw new InvalidOperationException(string.Join("; ",
                analysis.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.Message)));
        return (result.Root!, analysis);
    }
}