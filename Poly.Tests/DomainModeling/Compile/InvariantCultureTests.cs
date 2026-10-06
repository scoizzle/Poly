using System.Globalization;

using Poly.Analysis;
using Poly.DomainModeling;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Ontology;
using Poly.Interpretation.CSharp;

namespace Poly.Tests.DomainModeling.Compile;

/// <summary>
/// The DSL and printed C# are fixed formats: numbers use '.' as the decimal separator
/// whatever the current culture. These run under de-DE, where the current culture writes
/// 0.5 as "0,5".
/// </summary>
public class InvariantCultureTests {
    private const string Poly = """
        domain Shop
        Item: entity {
          Price: Number range(0.01, 99.5)
          Cheap: policy { Price < 0.5 }
          Draft: stage {
            Sell: action require Cheap { transition to Sold }
          }
          Sold: stage { }
        }
        """;

    [Test]
    public async Task PrintedDsl_UnderGermanCulture_KeepsDecimalPoints_AndReparses() {
        using var german = new GermanCultureScope();
        var (domain, _, _) = Evolve(Poly);

        var printed = new DomainDslPrinter().Print(domain);
        var (reparsed, _, _) = Evolve(printed);

        await Assert.That(printed).Contains("range(0.01, 99.5)");
        await Assert.That(printed).Contains("Price < 0.5");
        await Assert.That(new DomainDslPrinter().Print(reparsed)).IsEqualTo(printed);
    }

    [Test]
    public async Task PrintedCsharp_UnderGermanCulture_KeepsDecimalPoints_AndCompiles() {
        using var german = new GermanCultureScope();
        var (domain, analysis, session) = Evolve(Poly);

        var cs = new CSharpGenerator().Generate(session.Lower(domain, analysis));

        await Assert.That(cs).Contains("0.5");
        await Assert.That(cs).Contains("'Price' must be >= 0.01.");
        await Assert.That(cs).Contains("'Price' must be <= 99.5.");
        WhenAnySimulatePrintAgreeTests.CompileGenerated(cs, "InvariantCulturePrint");
    }

    private static (Domain Domain, AnalysisResult Analysis, DomainSession Session) Evolve(string poly) {
        var session = DomainSession.ForSource(poly, ExtensionCatalog.ProductAuthoring);
        var changes = new PolyDslParser(poly, session).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(changes, session: session);
        if (!result.Succeeded)
            throw new InvalidOperationException(result.FailureSummary);
        return (result.Root!, result.Analysis, session);
    }

    // Sets de-DE for the current test's async flow and restores the previous cultures.
    private sealed class GermanCultureScope : IDisposable {
        private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
        private readonly CultureInfo _uiCulture = CultureInfo.CurrentUICulture;

        public GermanCultureScope() {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");
        }

        public void Dispose() {
            CultureInfo.CurrentCulture = _culture;
            CultureInfo.CurrentUICulture = _uiCulture;
        }
    }
}
