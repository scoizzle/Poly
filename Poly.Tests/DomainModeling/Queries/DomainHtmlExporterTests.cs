using System.Text.RegularExpressions;

using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Queries;

using Entity = Poly.DomainModeling.Ontology.Entity;

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

    private const string Shop = """
        domain Shop
        Order: entity {
          Total: Number range(0, 500) default(0)
          Big: policy { Total > 100 }
          Small: policy { Total < 20 }
          Draft: stage {
            Submit: action
              require Big {
              transition to Active
            }
          }
          Active: stage {
          }
        }
        """;

    private const string Gate = """
        domain Gate
        Order: entity {
          Total: Number range(0, 500) default(0)
          Big: policy { Total > 100 }
          Small: policy { Total < 20 }
          Draft: stage require Small {
            Submit: action
              require Small {
              transition to Active
            }
          }
          Active: stage {
            Submit: action
              require Big {
              assign Total to 0
            }
          }
          Reset: action
            require Big {
            assign Total to 0
          }
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

    [Test]
    public async Task Export_GuardsColumn_StaysNamesOnly() {
        var html = Export(Shop);

        await Assert.That(html).Contains("<tr><td>Submit</td><td>Draft</td><td>—</td><td>Big</td></tr>")
            .Because("the Guards column lists policy names only; conditions live in the Policies table");
    }

    [Test]
    public async Task Export_WithoutPolicies_HasNoPoliciesSection() {
        var html = Export(Catalog);

        await Assert.That(html).Contains("<h3>Stages</h3>")
            .Because("the same output still renders its other sections");
        await Assert.That(html).DoesNotContain("<h3>Policies</h3>");
    }

    private static string[] PolicyRows(string html) {
        var start = html.IndexOf("<h3>Policies</h3>", StringComparison.Ordinal);
        if (start < 0)
            return [];
        var end = html.IndexOf("</tbody></table>", start, StringComparison.Ordinal);
        return [.. html[start..end].Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.StartsWith("<tr><td>"))];
    }

    private static string Export(Domain domain) => DomainHtmlExporter.Export(domain);

    private static Domain WithEntityPolicies(string dsl, params Policy[] policies) {
        var domain = EvolvedDomain.FromDsl(dsl).Domain;
        return domain with { Types = [.. domain.Types.Select(t => t is Entity e ? e with { Policies = policies } : t)] };
    }

    [Test]
    public async Task Export_ListsEveryPolicyWithItsCondition() {
        var html = Export(Shop);

        await Assert.That(html).Contains("<h3>Policies</h3>");
        await Assert.That(PolicyRows(html).SequenceEqual(new[] {
            "<tr><td>Big</td><td>entity</td><td><code>Total &gt; 100</code></td></tr>",
            "<tr><td>Small</td><td>entity</td><td><code>Total &lt; 20</code></td></tr>",
            "<tr><td>Big</td><td>action:Draft.Submit</td><td><code>Total &gt; 100</code></td></tr>",
        })).IsTrue();
    }

    [Test]
    public async Task Export_PolicyScopes_FollowDeclarationOrder() {
        var html = Export(Gate);

        await Assert.That(PolicyRows(html).SequenceEqual(new[] {
            "<tr><td>Big</td><td>entity</td><td><code>Total &gt; 100</code></td></tr>",
            "<tr><td>Small</td><td>entity</td><td><code>Total &lt; 20</code></td></tr>",
            "<tr><td>Small</td><td>stage:Draft</td><td><code>Total &lt; 20</code></td></tr>",
            "<tr><td>Small</td><td>action:Draft.Submit</td><td><code>Total &lt; 20</code></td></tr>",
            "<tr><td>Big</td><td>action:Active.Submit</td><td><code>Total &gt; 100</code></td></tr>",
            "<tr><td>Big</td><td>action:Reset</td><td><code>Total &gt; 100</code></td></tr>",
        })).IsTrue();
    }

    [Test]
    public async Task Export_PolicyCondition_UsesPrintedSpelling() {
        var domain = WithEntityPolicies("domain Spell\nItem: entity { Note: Text }",
            new Policy("Q", DomainExpression.Equal(DomainExpression.Property("Note"), DomainExpression.Literal("say \"hi\""))));

        await Assert.That(PolicyRows(Export(domain)).SequenceEqual(new[] {
            "<tr><td>Q</td><td>entity</td><td><code>Note is &quot;say \\&quot;hi\\&quot;&quot;</code></td></tr>",
        })).IsTrue();
    }

    [Test]
    [Arguments("<", "a&lt;b")]
    [Arguments(">", "a&gt;b")]
    [Arguments("&", "a&amp;b")]
    [Arguments("|", "a|b")]
    [Arguments("`", "a`b")]
    [Arguments("\"", "a&quot;b")]
    public async Task Export_PolicyNameWithSpecialCharacter_IsHtmlEncoded(string special, string cell) {
        var domain = WithEntityPolicies("domain Esc\nItem: entity { Total: Number }",
            new Policy("a" + special + "b", DomainExpression.LessThan(DomainExpression.Property("Total"), DomainExpression.Literal(5))));

        await Assert.That(PolicyRows(Export(domain)).SequenceEqual(new[] {
            $"<tr><td>{cell}</td><td>entity</td><td><code>Total &lt; 5</code></td></tr>",
        })).IsTrue();
    }
}