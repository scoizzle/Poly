using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Ontology;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// #367 C-2 relocation parity. The implicit-constraint envelope moved from the Product pack into the
/// <see cref="ActionParameterEnvelopeMetadata"/> analysis fact; the envelope goldens were captured from the
/// pack's original private derivation BEFORE the move, and the rendered Program.cs goldens from its
/// pre-relocation output. The program renders were regenerated for P-03 (#620): the only change is each action
/// handler's save moving inside its <c>if (result.IsSuccess)</c> branch. Neither the fact nor any other line of
/// the emitted program may differ from them.
/// </summary>
public class ActionParameterEnvelopeParityTests {
    public static IEnumerable<string> FixtureNames() => ActionParameterEnvelopeFixtures.All.Select(f => f.Name);

    private static string Golden(string name, string suffix) =>
        File.ReadAllText(Path.Combine(ActionParameterEnvelopeFixtures.GoldenDirectory(), name + suffix));

    private static Domain Fixture(string name) =>
        ActionParameterEnvelopeFixtures.ParseDomain(ActionParameterEnvelopeFixtures.All.Single(f => f.Name == name).Poly);

    [Test]
    [MethodDataSource(nameof(FixtureNames))]
    public async Task Fact_EqualsEnvelopeThePackPreviouslyDerived(string name) {
        var domain = Fixture(name);
        var analysis = DomainModelAnalyzer.Analyze(domain);
        var lines = new List<string>();
        foreach (var entity in domain.Types.OfType<Entity>())
            foreach (var action in entity.Actions)
                foreach (var parameter in action.Parameters) {
                    var envelope = analysis.GetActionParameterEnvelope(action, parameter.Name)
                        ?? throw new InvalidOperationException($"No envelope published for {entity.Name}.{action.Name}.");
                    lines.Add($"{entity.Name}.{action.Name}({parameter.Name}): {ActionParameterEnvelopeFixtures.Format(envelope)}");
                }

        await Assert.That(string.Join("\n", lines) + "\n").IsEqualTo(Golden(name, ".envelope.txt"));
    }

    [Test]
    [MethodDataSource(nameof(FixtureNames))]
    public async Task EmittedProgram_IsByteIdenticalToPreRelocationOutput(string name) {
        var rendered = GenerationAssertions.MinimalApiIr(Fixture(name)).Render();
        await Assert.That(rendered).IsEqualTo(Golden(name, ".program.cs.txt"));
    }

    [Test]
    public async Task Pass_RunsAfterStructureAndInvariantsBeforeEffectAnalyzer() {
        var analysis = DomainModelAnalyzer.Analyze(Fixture("unconditional_assign"));
        var order = analysis.Telemetry.Passes.Select(p => p.PassName).ToList();
        int Index(string id) => order.IndexOf(id) is var i and >= 0 ? i : throw new Exception($"Pass '{id}' missing.");
        await Assert.That(Index(ActionParameterEnvelopePass.Id)).IsGreaterThan(Index(EntityStructureAnalyzer.Id));
        await Assert.That(Index(ActionParameterEnvelopePass.Id)).IsGreaterThan(Index(EffectInvariantAnalyzer.Id));
        await Assert.That(Index(ActionParameterEnvelopePass.Id)).IsLessThan(Index(EffectAnalyzer.Id));
    }

    [Test]
    public async Task Conditional_AndDerivedAndInvokeFlows_ContributeNothing() {
        var d = Fixture("conditional_assign");
        var analysis = DomainModelAnalyzer.Analyze(d);
        var book = d.Types.OfType<Entity>().Single();
        await Assert.That(analysis.GetActionParameterEnvelope(book.Actions.Single(a => a.Name == "Restock"), "amount")!.Count).IsEqualTo(0);
        await Assert.That(analysis.GetActionParameterEnvelope(book.Actions.Single(a => a.Name == "Either"), "amount")!.Count).IsEqualTo(0);

        var shifted = Fixture("shifted_offset");
        var shiftedAnalysis = DomainModelAnalyzer.Analyze(shifted);
        var shift = shifted.Types.OfType<Entity>().Single().Actions.Single();
        await Assert.That(shiftedAnalysis.GetActionParameterEnvelope(shift, "p")!.Count).IsEqualTo(0);

        var chain = Fixture("invoke_chain");
        var chainAnalysis = DomainModelAnalyzer.Analyze(chain);
        var ship = chain.Types.OfType<Entity>().Single().Actions.Single(a => a.Name == "Ship");
        await Assert.That(chainAnalysis.GetActionParameterEnvelope(ship, "amt")!.Count).IsEqualTo(0);
    }
}