using Poly.Ast.Nodes;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// Create-in lowers without a constraint probe. A missing domain still fails closed.
/// A missing analysis fails closed: the compiled entity has no dictionary create.
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

    static void LowerAddKid(bool withDomain, bool withAnalysis) {
        var (domain, analysis) = EvolvedDomain.FromDsl(Dsl);
        var parent = domain.Types.OfType<Entity>().Single(e => e.Name == "Parent");
        var pass = new EffectLoweringPass(parent, new LoweringContext(
            new Parameter("entity", new TypeReference(parent.Name)),
            Domain: withDomain ? domain : null,
            Analysis: withAnalysis ? analysis : null));
        pass.LowerActionBody(parent.Actions.Single(a => a.Name == "AddKid").Effects);
    }

    [Test]
    [Arguments(false, true)]
    [Arguments(false, false)]
    public async Task LowerActionBody_CreateInWithoutDomain_Throws(bool withDomain, bool withAnalysis) =>
        await Assert.That(() => LowerAddKid(withDomain, withAnalysis)).Throws<InvalidOperationException>();

    [Test]
    public async Task LowerActionBody_CreateInWithoutAnalysis_Throws() =>
        await Assert.That(() => LowerAddKid(withDomain: true, withAnalysis: false))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("no dictionary create member");

    [Test]
    public async Task LowerActionBody_CreateInWithDomainAndAnalysis_Lowers() =>
        await Assert.That(() => LowerAddKid(withDomain: true, withAnalysis: true)).ThrowsNothing();
}