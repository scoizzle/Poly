using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Bootstrap;

namespace Poly.Tests.TestHelpers;

public class ValidDomainTests {
    static Entity EntityWith(params string[] typeNames) =>
        new("Item", [.. typeNames.Select((t, i) => new Property($"P{i}", new DomainTypeReference(t), []))], [], [], []);

    static IEnumerable<string> Errors(Domain domain) =>
        DomainModelAnalyzer.Analyze(domain).Diagnostics
            .Where(d => d.Severity == Poly.Analysis.DiagnosticSeverity.Error)
            .Select(d => d.Message);

    [Test]
    public async Task Create_WithEveryCanonicalType_AnalyzesWithoutErrors() {
        var names = CanonicalBuiltInTypeCatalog.Definitions.Select(d => d.Name).ToArray();
        await Assert.That(names).IsEquivalentTo(["Boolean", "Number", "Text", "Uuid", "Binary"]);
        await Assert.That(Errors(ValidDomain.Create("T", [EntityWith(names)]))).IsEmpty();
    }

    [Test]
    public async Task Create_WithDateAndTheTemporalExtension_AnalyzesWithoutErrors() =>
        await Assert.That(Errors(ValidDomain.Create("T", [EntityWith("Text", "Date")],
            extensions: [ExtensionCatalog.TemporalId]))).IsEmpty();

    // Extensions default to none, so a temporal type is unknown until the test imports temporal.
    [Test]
    public async Task Create_WithDateButNoExtension_ReportsUnknownDate() =>
        await Assert.That(Errors(ValidDomain.Create("T", [EntityWith("Text", "Date")])))
            .IsEquivalentTo(["Property 'P1' references unknown type 'Date'."]);
}
