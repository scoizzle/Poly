using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;

namespace Poly.Tests.DomainModeling.Runtime;

/// <summary>
/// The runtime entity type forwards its store jobs to <see cref="DomainInstanceHostJobs"/>.
/// Every job needs a domain instance, and the name/value-pair create calls the lowering
/// makes (one typed pair, or up to 16 object pairs) reach the same values bag.
/// </summary>
public sealed class DomainInstanceHostJobsTests {
    private static readonly Dictionary<string, object?> NotAnInstance = new();
    private static readonly Dictionary<string, object?> NoValues = new();

    public static IEnumerable<Func<(string Job, System.Action Call)>> EveryJob() {
        yield return () => ("EnsureUnique", () => DomainInstanceHostJobs.EnsureUnique(NotAnInstance, "Name", (object?)"x"));
        yield return () => ("EnsureUnique", () => DomainInstanceHostJobs.EnsureUnique(NotAnInstance, "Count", 1L));
        yield return () => ("Notify", () => DomainInstanceHostJobs.Notify(NotAnInstance, "Open"));
        yield return () => ("Notify", () => DomainInstanceHostJobs.Notify(NotAnInstance, "Open", "Draft"));
        yield return () => ("LinkRelated", () => DomainInstanceHostJobs.LinkRelated(NotAnInstance, "lines", null));
        yield return () => ("Create", () => DomainInstanceHostJobs.Create(NotAnInstance, "Line", NoValues));
        yield return () => ("CreateIn", () => DomainInstanceHostJobs.CreateIn(NotAnInstance, "lines", NoValues));
        yield return () => ("ProbeCreate", () => DomainInstanceHostJobs.ProbeCreate(NotAnInstance, "Line", NoValues));
    }

    [Test]
    [MethodDataSource(nameof(EveryJob))]
    public async Task Job_OnNonInstanceReceiver_Throws(string job, System.Action call) {
        await Assert.That(call).Throws<InvalidOperationException>()
            .WithMessageContaining($"{job} requires a domain instance, got Dictionary");
    }

    private static readonly string[] Extra = [.. Enumerable.Range(1, 13).Select(i => $"F{i}")];

    private static (Entity Maker, Domain Domain) MakerDomain() {
        var extraProps = string.Join("\n", Extra.Select(f => $"  {f}: Number default(0)"));
        var extraInits = string.Join(" ", Extra.Select((f, i) => $"{f}: {i + 1}"));
        var poly = $$"""
            domain Shop
            Target: entity {
              N: Number default(0)
              T: Text default("")
              B: Boolean default(false)
            {{extraProps}}
            }
            Maker: entity {
              MakeN: action { create Target { N: 5 } }
              MakeT: action { create Target { T: "x" } }
              MakeB: action { create Target { B: true } }
              MakeAll: action { create Target { N: 5 T: "x" B: true {{extraInits}} } }
            }
            """;
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(new PolyDslParser(poly).Parse());
        if (!result.Succeeded)
            throw new InvalidOperationException(result.FailureSummary);
        return (result.Root!.Types.OfType<Entity>().Single(e => e.Name == "Maker"), result.Root!);
    }

    private static DomainEntityInstance Make(string action) {
        var (maker, domain) = MakerDomain();
        var instance = DomainEntityInstance.Create(maker, domain: domain);
        new DomainInstanceStore().Add(instance);
        var result = instance.InvokeAction(action);
        if (!result.Succeeded)
            throw new InvalidOperationException(result.ErrorMessage);
        return instance.CreatedChildren.Single();
    }

    [Test]
    public async Task Create_OneTypedPair_StoresThatValue() {
        await Assert.That(Make("MakeN").GetProperty<object>("N")).IsEqualTo(5L);
        await Assert.That(Make("MakeT").GetProperty<object>("T")).IsEqualTo("x");
        await Assert.That(Make("MakeB").GetProperty<object>("B") is true).IsTrue();
    }

    [Test]
    public async Task Create_SixteenPairs_StoresEveryValue() {
        var created = Make("MakeAll");

        await Assert.That(created.GetProperty<object>("N")).IsEqualTo(5L);
        await Assert.That(created.GetProperty<object>("T")).IsEqualTo("x");
        await Assert.That(created.GetProperty<object>("B") is true).IsTrue();
        for (var i = 0; i < Extra.Length; i++)
            await Assert.That(created.GetProperty<object>(Extra[i])).IsEqualTo((long)(i + 1));
    }
}
