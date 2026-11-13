using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Meaning;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Effects;

using DmAction = Poly.DomainModeling.Ontology.Action;
using DmPrimitive = Poly.DomainModeling.Ontology.PrimitiveType;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// The consumer contract of <see cref="DomainTypeModelMetadata"/> (T-04): after the <c>DomainExpressionType</c>
/// pass the Domain node carries the model, which answers the numeric kind of a property (what V-02 reads); a walk
/// with no Domain root has no type lookup, so there is no model and no stamp, and the analyzer still checks
/// names through its legacy table.
/// </summary>
public class DomainTypeModelMetadataTests {
    private static Entity Item() => new("Item",
        [new Property("Count", new DomainTypeReference("Number"), []),
         new Property("Amount", new DomainTypeReference("Decimal"), []),
         new Property("Total", new DomainTypeReference("Number"), [])],
        [new DmAction("Set", InvocationResult.Void, [],
            [new AssignEffect(DomainExpression.Property("Total"), DomainExpression.Property("Amount"))], [])],
        [], []);

    private static Domain Dom() => new("T", [
        new DmPrimitive("Number", PrimitiveSemantics.Number, []),
        new DmPrimitive("Decimal", PrimitiveSemantics.Decimal, []),
        Item()]);

    [Test]
    public async Task AfterAnalysis_TheDomainNodeCarriesTheModel_AndItAnswersTheNumericKind() {
        var domain = Dom();
        var analysis = DomainModelAnalyzer.Analyze(domain);

        var stamp = analysis.GetMetadata<DomainTypeModelMetadata>(domain);
        await Assert.That(stamp).IsNotNull();
        var item = domain.Types.OfType<Entity>().Single();
        var model = stamp!.Model;
        await Assert.That(model.KindOf(model.OfProperty(item.Properties.Single(p => p.Name == "Count")))).IsEqualTo(NumericKind.Integer64);
        await Assert.That(model.KindOf(model.OfProperty(item.Properties.Single(p => p.Name == "Amount")))).IsEqualTo(NumericKind.Decimal);
    }

    [Test]
    public async Task AWalkWithNoDomainRoot_HasNoModelStamp_AndReportsWhatItCannotType() {
        // T-05b: the analyzer's own name table is gone (one resolver: the model). With no domain there is no model,
        // so no name types and the walk fails closed with DMSEM006 instead of checking names through a second table.
        var item = Item();
        var analysis = new Poly.Analysis.AnalyzerBuilder()
            .AddAnalyzer(new DomainCatalogPass())
            .AddAnalyzer(new ExpressionTypeAnalyzer())
            .Build()
            .Analyze(item);

        await Assert.That(analysis.GetMetadata<DomainTypeModelMetadata>(item)).IsNull();
        await Assert.That(analysis.Diagnostics.Any(d => d.Code == "DMSEM006")).IsTrue();
        await Assert.That(analysis.Diagnostics.Any(d => d.Code == DomainModelDiagnosticCodes.SemanticTypeCompatibility)).IsFalse();
    }
}