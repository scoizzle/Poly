using System.Text.RegularExpressions;

using Poly.DomainModeling.Queries;

namespace Poly.Tests.DomainModeling.Queries;

/// <summary>
/// The human-facing docs artifact: one self-contained HTML file a person can open with no
/// server and no scripts. It carries the same facts and terms the machine exports use.
/// </summary>
public class DomainHtmlExporterTests {
    private const string Catalog = """
        domain Catalog

        Color: enum iri("https://example.com/Color") {
          Red iri("https://example.com/Color/Red") term("red"),
          Green,
        }

        Money: value {
          Amount: Number range(0, 100)
        }

        Line: entity {
          Sku: Text
        }

        Product: entity iri("https://example.com/Product") label("Product") description("A sellable item") {
          Total: Number range(0, 100) iri("https://example.com/Product/total")
          Shade: Color default(Red)
          Note: Text optional
          Draft: stage {
            Submit: action { transition to Active }
          }
          Active: stage {}
          lines: many Line
        }
        """;

    private static string Export(string poly) =>
        DomainHtmlExporter.Export(EvolvedDomain.FromDsl(poly).Domain);

    [Test]
    public async Task Export_EmitsSelfContainedDocumentWithEntityFacts() {
        var html = Export(Catalog);

        await Assert.That(html).StartsWith("<!doctype html>");
        await Assert.That(html).Contains("<title>Catalog — Poly ontology</title>");
        await Assert.That(html).Contains("<h2>Product</h2>");
        await Assert.That(html).Contains("https://example.com/Product");
        await Assert.That(html).Contains("A sellable item");
        await Assert.That(html).Contains("<code>range(0, 100)</code>");
        await Assert.That(html).Contains("class=\"req\"");
        await Assert.That(html).Contains("class=\"opt\"");
        await Assert.That(html).Contains("Submit");
        await Assert.That(html).Contains("Draft");
    }

    [Test]
    public async Task Export_EscapesModelAuthoredText() {
        var html = Export("""
            domain Dangerous
            Item: entity description("A <script>alert('x')</script> & item") {
              Name: Text
            }
            """);

        await Assert.That(html).Contains("&lt;script&gt;");
        await Assert.That(html).Contains("&amp;");
        await Assert.That(html).DoesNotContain("<script");
        await Assert.That(html).DoesNotContain("</script");
        await Assert.That(html).DoesNotContain("<link");
        await Assert.That(html).DoesNotContain(" src=");
        await Assert.That(html).DoesNotContain("http-equiv");
    }

    [Test]
    public async Task Export_IncludesEnumValueTypeAndRelationships() {
        var html = Export(Catalog);

        await Assert.That(html).Contains("id=\"enum-Color\"");
        await Assert.That(html).Contains("https://example.com/Color/Red");
        await Assert.That(html).Contains("<code>red</code>");
        await Assert.That(html).Contains("id=\"value-Money\"");
        await Assert.That(html).Contains("id=\"relationships\"");
        await Assert.That(html).Contains("one to many");
        await Assert.That(html).Contains("lines");
    }

    [Test]
    public async Task Export_MarksRequiredOptionalDefaultAndEntryAssignedRows() {
        var html = Export("""
            domain Counter
            Counter: entity {
              Total: Number
              Frozen: Number
              Note: Text optional
              Shade: Text default("x")
              Open: stage {
                entry { assign Frozen to 0 }
                Close: action { transition to Closed }
              }
              Closed: stage {}
            }
            """);

        await Assert.That(Row(html, "Total")).Contains("class=\"req\"");
        await Assert.That(Row(html, "Frozen")).Contains("class=\"opt\"")
            .Because("the first stage's entry effect initializes it");
        await Assert.That(Row(html, "Note")).Contains("class=\"opt\"");
        await Assert.That(Row(html, "Shade")).Contains("class=\"opt\"");
    }

    private static string Row(string html, string property) =>
        Regex.Match(html, $"(?s)<tr>\\s*<td>{Regex.Escape(property)}</td>.*?</tr>").Value;

    [Test]
    public async Task Export_IsDeterministic() {
        var first = Export(Catalog);
        var second = Export(Catalog);

        await Assert.That(second).IsEqualTo(first);
    }
}