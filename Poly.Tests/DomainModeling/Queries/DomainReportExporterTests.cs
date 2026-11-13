using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Queries;

using Entity = Poly.DomainModeling.Ontology.Entity;

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

    [Test]
    public async Task Export_GuardsColumn_StaysNamesOnly() {
        var report = Export(Shop);

        await Assert.That(report).Contains("| Submit | Draft | — | Big |")
            .Because("the Guards column lists policy names only; conditions live in the Policies table");
    }

    [Test]
    public async Task Export_WithoutPolicies_HasNoPoliciesSection() {
        var report = Export(Catalog);

        await Assert.That(report).Contains("**Stages**")
            .Because("the same output still renders its other sections");
        await Assert.That(report).DoesNotContain("**Policies**");
    }

    private static string[] PolicyRows(string report) {
        var lines = report.Split('\n');
        var start = Array.IndexOf(lines, "**Policies**");
        return start < 0 ? [] : [.. lines.Skip(start + 4).TakeWhile(l => l.StartsWith("| "))];
    }

    private static string Export(Domain domain) => DomainReportExporter.Export(domain);

    private static Domain WithEntityPolicies(string dsl, params Policy[] policies) {
        var domain = EvolvedDomain.FromDsl(dsl).Domain;
        return domain with { Types = [.. domain.Types.Select(t => t is Entity e ? e with { Policies = policies } : t)] };
    }

    [Test]
    public async Task Export_ListsEveryPolicyWithItsCondition() {
        var report = Export(Shop);

        await Assert.That(report).Contains("**Policies**");
        await Assert.That(PolicyRows(report).SequenceEqual(new[] {
            "| Big | entity | `Total > 100` |",
            "| Small | entity | `Total < 20` |",
            "| Big | action:Draft.Submit | `Total > 100` |",
        })).IsTrue();
    }

    [Test]
    public async Task Export_PolicyScopes_FollowDeclarationOrder() {
        var report = Export(Gate);

        await Assert.That(PolicyRows(report).SequenceEqual(new[] {
            "| Big | entity | `Total > 100` |",
            "| Small | entity | `Total < 20` |",
            "| Small | stage:Draft | `Total < 20` |",
            "| Small | action:Draft.Submit | `Total < 20` |",
            "| Big | action:Active.Submit | `Total > 100` |",
            "| Big | action:Reset | `Total > 100` |",
        })).IsTrue();
    }

    [Test]
    public async Task Export_PolicyCondition_UsesPrintedSpelling() {
        var domain = WithEntityPolicies("domain Spell\nItem: entity { Note: Text }",
            new Policy("Q", DomainExpression.Equal(DomainExpression.Property("Note"), DomainExpression.Literal("say \"hi\""))));

        await Assert.That(PolicyRows(Export(domain)).SequenceEqual(new[] {
            "| Q | entity | `Note is \"say \\\"hi\\\"\"` |",
        })).IsTrue();
    }

    [Test]
    public async Task Export_NewlineInStringLiteral_DoesNotSplitTheRow() {
        var domain = WithEntityPolicies("domain Spell\nItem: entity { Note: Text }",
            new Policy("N", DomainExpression.Equal(DomainExpression.Property("Note"), DomainExpression.Literal("a\nb"))));

        await Assert.That(PolicyRows(Export(domain)).SequenceEqual(new[] {
            "| N | entity | `Note is \"a b\"` |",
        })).IsTrue();
    }

    [Test]
    [Arguments("<", "a<b")]
    [Arguments(">", "a>b")]
    [Arguments("&", "a&b")]
    [Arguments("|", "a\\|b")]
    [Arguments("`", "a`b")]
    [Arguments("\"", "a\"b")]
    public async Task Export_PolicyNameWithSpecialCharacter_StaysOneEscapedRow(string special, string cell) {
        var domain = WithEntityPolicies("domain Esc\nItem: entity { Total: Number }",
            new Policy("a" + special + "b", DomainExpression.LessThan(DomainExpression.Property("Total"), DomainExpression.Literal(5))));

        await Assert.That(PolicyRows(Export(domain)).SequenceEqual(new[] {
            $"| {cell} | entity | `Total < 5` |",
        })).IsTrue();
    }
}