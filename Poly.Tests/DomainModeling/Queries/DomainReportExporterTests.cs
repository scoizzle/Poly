using Poly.DomainModeling.Queries;

namespace Poly.Tests.DomainModeling.Queries;

/// <summary>
/// The human-facing artifact: one Markdown report a person can read without knowing the
/// DSL. It must carry the same terms and constraints the exports do, not a second summary
/// that can drift.
/// </summary>
public class DomainReportExporterTests {
    private const string Catalog = """
        domain Catalog

        Color: enum {
          Red iri("https://example.com/Color/Red") term("red"),
          Green,
        }

        Line: entity {
          Sku: Text
        }

        Product: entity iri("https://example.com/Product") label("Product") description("A sellable item") {
          Sku: Text iri("https://example.com/Product/sku") length(1, 20)
          Note: Text optional
          Shade: Color default(Red)
          Draft: stage {
            Submit: action { transition to Active }
          }
          Active: stage {}
          lines: many Line
        }
        """;

    private static string Export(string poly) =>
        DomainReportExporter.Export(EvolvedDomain.FromDsl(poly).Domain);

    [Test]
    public async Task Export_IncludesDomainCountsAndEntityTerms() {
        var report = Export(Catalog);

        await Assert.That(report).Contains("# Catalog");
        await Assert.That(report).Contains("2 entities");
        await Assert.That(report).Contains("1 enum");
        await Assert.That(report).Contains("4 properties")
            .Because("the count line must read as English, not 'propertys'");
        await Assert.That(report).Contains("### Product");
        await Assert.That(report).Contains("https://example.com/Product");
        await Assert.That(report).Contains("A sellable item");
    }

    [Test]
    public async Task Export_IncludesPropertyConstraintsAndRequiredness() {
        var report = Export(Catalog);

        await Assert.That(report).Contains("| Sku | Text | `length(1, 20)` | yes |");
        await Assert.That(report).Contains("| Note | Text | `optional` | no |")
            .Because("`optional` is the create opt-out");
        await Assert.That(report).Contains("`default(Red)`")
            .Because("a default is shown and makes the property omittable");
    }

    [Test]
    public async Task Export_IncludesStagesActionsEnumsAndRelationships() {
        var report = Export(Catalog);

        await Assert.That(report).Contains("### Color");
        await Assert.That(report).Contains("https://example.com/Color/Red");
        await Assert.That(report).Contains("| Red |");
        await Assert.That(report).Contains("| Draft |");
        await Assert.That(report).Contains("Submit");
        await Assert.That(report).Contains("| one to many |");
        await Assert.That(report).Contains("| Product | lines | Line |");
    }

    [Test]
    public async Task Export_EscapesPipesAndBackticksInConstraints() {
        var report = Export("""
            domain Tick
            P: entity { Code: Text pattern("^(a|b)$") }
            """);
        var row = report.Split('\n').Single(l => l.StartsWith("| Code |"));
        await Assert.That(row).Contains("\\|")
            .Because("a raw pipe would split the Markdown table row");
        await Assert.That(row).Contains("`pattern(\"^(a\\|b)$\")`");

        var tick = Export("domain Tick\nP: entity { Code: Text pattern(\"a`b\") }");
        await Assert.That(tick).Contains("``pattern(\"a`b\")``")
            .Because("the code fence must outrun the backtick inside the content");
    }

    [Test]
    public async Task Export_IsDeterministic() {
        var first = Export(Catalog);
        var second = Export(Catalog);

        await Assert.That(second).IsEqualTo(first);
    }
}