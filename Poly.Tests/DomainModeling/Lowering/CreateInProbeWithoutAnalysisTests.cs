using Poly.Ast.Nodes;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// Create-in lowers without a constraint probe. A context with no domain or no analysis is
/// not representable any more: <see cref="LoweringContext"/> requires both (see
/// <see cref="LoweringContextBoundaryTests"/>), so the old "missing domain" and "missing
/// analysis" cases have no state left to test.
/// </summary>
public class CreateInProbeWithoutAnalysisTests {
    const string Dsl = """
        domain Shop
        Kid: entity {
          Name: Text
        }
        Parent: entity {
          Name: Text
          kids: many Kid
          AddKid: action { create in kids { Name: "x" } }
        }
        """;

    static void LowerAddKid() {
        var (domain, analysis) = EvolvedDomain.FromDsl(Dsl);
        var parent = domain.Types.OfType<Entity>().Single(e => e.Name == "Parent");
        var pass = new EffectLoweringPass(parent, new LoweringContext(
            new Parameter("entity", new TypeReference(parent.Name)),
            analysis,
            domain));
        pass.LowerActionBody(parent.Actions.Single(a => a.Name == "AddKid").Effects);
    }

    [Test]
    public async Task LowerActionBody_CreateInWithDomainAndAnalysis_Lowers() =>
        await Assert.That(LowerAddKid).ThrowsNothing();
}