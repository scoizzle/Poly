using Poly.DomainModeling;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;

namespace Poly.Tests.DomainModeling.Evolution;

/// <summary>#464: AddEffectToAction can target one stage when two stages share an action name.</summary>
public class StageScopedActionEvolutionTests {
    [Test]
    public async Task AddEffectToAction_WithStageName_OnlyThatStageActionGetsTheEffect() {
        var evolution = new DomainEvolution(new Domain("_", []));
        var result = evolution.Evolve()
            .AddEntity("Order")
            .AddStage("Order", "Draft")
            .AddStage("Order", "Active")
            .AddStage("Order", "Closed")
            .AddActionToStage("Order", "Draft", "Cancel")
            .AddActionToStage("Order", "Active", "Cancel")
            .AddEffectToAction("Order", "Cancel", new StageTransitionEffect(new StageReference("Closed")), "Active")
            .Apply();

        await Assert.That(result.Succeeded).IsTrue()
            .Because(string.Join("; ", result.Analysis.Diagnostics.Select(d => d.Message).Concat(result.MutationErrors)));
        var order = result.Root!.Types.OfType<Entity>().Single(e => e.Name == "Order");
        var draft = order.Stages.Single(s => s.Name == "Draft").Actions.Single(a => a.Name == "Cancel");
        var active = order.Stages.Single(s => s.Name == "Active").Actions.Single(a => a.Name == "Cancel");
        await Assert.That(active.Effects.Count).IsEqualTo(1);
        await Assert.That(draft.Effects.Count).IsEqualTo(0);
    }
}