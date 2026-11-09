using Poly.Ast.Nodes;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Effects;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>#409: the general <c>for</c> has no lowering yet and must fail loud, never lower to nothing.</summary>
public class GeneralForLoweringTests {
    private static EffectLoweringPass Pass() =>
        new(new Entity("Source", [], Actions: [], [], []), new LoweringContext(new Parameter("entity")));

    [Test]
    public async Task GeneralFor_Route_Throws_InsteadOfLoweringToNull() {
        var effect = new ForEachEffect("line", "lines", null, []);

        var ex = Assert.Throws<NotSupportedException>(() => Pass().TryLowerVmNode(effect));

        await Assert.That(ex.Message).Contains("DMEFF016");
        await Assert.That(ex.Message).Contains("not lowerable yet");
    }
}