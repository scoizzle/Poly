using Poly.Ast.Nodes;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Effects;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>#409 slice 6: the general <c>for</c> lowers to a loop; it never lowers to nothing.</summary>
public class GeneralForLoweringTests {
    private static EffectLoweringPass Pass() =>
        new(new Entity("Source", [], Actions: [], [], []), new LoweringContext(new Parameter("entity")));

    [Test]
    public async Task GeneralFor_Route_LowersToForEachLoop() {
        var effect = new ForEachEffect("line", "lines", null, []);

        var node = Pass().TryLowerVmNode(effect);

        await Assert.That(node).IsNotNull();
        await Assert.That(((Block)node!).Nodes.Single()).IsTypeOf<ForEachLoop>();
    }
}