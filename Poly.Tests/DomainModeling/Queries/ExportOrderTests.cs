using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using Poly.DomainModeling;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Queries;
using Poly.Tests.TestHelpers;

using TUnit.Assertions.Enums;

using Entity = Poly.DomainModeling.Ontology.Entity;
using EnumType = Poly.DomainModeling.Ontology.EnumType;
using ValueType = Poly.DomainModeling.Ontology.ValueType;

namespace Poly.Tests.DomainModeling.Queries;

/// <summary>
/// D-01 (#688, Q12): every document projection lists types and members in declaration order.
/// The fixture's declaration order <c>[Flavour, Money, Zebra, Apple]</c> differs from its ordinal
/// order <c>[Apple, Flavour, Money, Zebra]</c>, so every assertion discriminates.
/// </summary>
public sealed class ExportOrderTests {
    private const string OrderFixture = """
        domain Ordering

        Flavour: enum {
          Beta term("beta"),
          Alpha term("alpha"),
        }

        Money: value {
          Zed: Number
          Abe: Number
        }

        Zebra: entity {
          Total: Number
          Note: Text optional
          Qty: Number
          Shade: Flavour
          apples: many Apple
        }

        Apple: entity {
          Qty: Number
          Note: Text optional
          parent: one Zebra
        }
        """;

    private static readonly string[] AllTypes = ["Flavour", "Money", "Zebra", "Apple"];
    private static readonly string[] ZebraMembers = ["Total", "Note", "Qty", "Shade", "apples"];
    private static readonly string[] AppleMembers = ["Qty", "Note", "parent"];

    private static Domain Fixture() => EvolvedDomain.FromDsl(OrderFixture).Domain;

    // ---- extractors --------------------------------------------------------------------

    private static string Section(string text, string heading) {
        var start = text.IndexOf(heading + "\n", StringComparison.Ordinal);
        if (start < 0)
            throw new InvalidOperationException($"No section '{heading}'.");
        var rest = text[(start + heading.Length)..];
        var level = heading.IndexOf(' ');
        var end = Regex.Match(rest, "\n#{1," + level + "} ");
        return end.Success ? rest[..end.Index] : rest;
    }

    private static List<string> RowNames(string report, string heading) =>
        [.. Section(report, heading).Split('\n')
            .Where(l => l.StartsWith("| ", StringComparison.Ordinal) && !l.StartsWith("| ---", StringComparison.Ordinal))
            .Skip(1)
            .Select(l => l.Split('|')[1].Trim())];

    private static List<string> RelationshipRows(string report) =>
        [.. Section(report, "## Relationships").Split('\n')
            .Where(l => l.StartsWith("| ", StringComparison.Ordinal) && !l.StartsWith("| ---", StringComparison.Ordinal))
            .Skip(1)
            .Select(l => { var c = l.Split('|'); return $"{c[1].Trim()}|{c[2].Trim()}"; })];

    private static List<string> Headings(string report, string section) =>
        [.. Regex.Matches(Section(report, section), @"(?m)^### (.+)$").Select(m => m.Groups[1].Value)];

    private static string HtmlSection(string html, string id) {
        var start = html.IndexOf($"<section id=\"{id}\">", StringComparison.Ordinal);
        if (start < 0)
            throw new InvalidOperationException($"No section '{id}'.");
        return html[start..html.IndexOf("</section>", start, StringComparison.Ordinal)];
    }

    private static List<string> Cells(string html, string id) =>
        [.. Regex.Matches(HtmlSection(html, id), @"<tr>\s*<td>([^<]*)</td>").Select(m => m.Groups[1].Value)];

    private static List<string> RelationshipCells(string html) =>
        [.. Regex.Matches(HtmlSection(html, "relationships"), @"<tr><td>([^<]*)</td><td>([^<]*)</td>")
            .Select(m => $"{m.Groups[1].Value}|{m.Groups[2].Value}")];

    private static List<string> SectionIds(string html) =>
        [.. Regex.Matches(html, "<section id=\"(entity|enum|value)-([^\"]+)\"").Select(m => m.Groups[2].Value)];

    private static List<string> Keys(JsonNode? node) =>
        [.. node!.AsObject().Select(p => p.Key)];

    private static string Tail(string iri) => iri[(iri.LastIndexOf('/') + 1)..];

    private static JsonArray Graph(string json) => JsonNode.Parse(json)!["@graph"]!.AsArray();

    private static List<string> LdClassNames(string json) =>
        [.. Graph(json).Where(n => n!["@type"]!.GetValue<string>() == "rdfs:Class")
            .Select(n => Tail(n!["@id"]!.GetValue<string>()))];

    private static List<string> LdPropertyNames(string json, string type) =>
        [.. Graph(json).Where(n => n!["@type"]!.GetValue<string>() == "rdf:Property"
                && Tail(n["rdfs:domain"]!["@id"]!.GetValue<string>()) == type)
            .Select(n => Tail(n!["@id"]!.GetValue<string>()))];

    private static List<string> LdEnumMemberIds(string json) =>
        [.. Graph(json).Where(n => n!["@type"]!.GetValue<string>().EndsWith("/Flavour", StringComparison.Ordinal))
            .Select(n => Tail(n!["@id"]!.GetValue<string>()))];

    private static List<string> ShapeNames(string json) =>
        [.. Graph(json).Where(n => n!["@type"]!.GetValue<string>() == "sh:NodeShape")
            .Select(n => Tail(n!["@id"]!.GetValue<string>()))];

    private static List<string> ShapePaths(string json, string type) =>
        [.. Graph(json).Single(n => Tail(n!["@id"]!.GetValue<string>()) == type
                && n["@type"]!.GetValue<string>() == "sh:NodeShape")!["sh:property"]!.AsArray()
            .Select(p => Tail(p!["sh:path"]!["@id"]!.GetValue<string>()))];

    // ---- tests -------------------------------------------------------------------------

    [Test]
    public async Task DeclarationOrder_ModelKeepsSourceOrder() {
        var domain = Fixture();
        var entities = domain.Types.OfType<Entity>().ToList();
        var zebra = entities.Single(e => e.Name == "Zebra");

        await Assert.That(entities.Select(e => e.Name).ToList())
            .IsEquivalentTo(new[] { "Zebra", "Apple" }, CollectionOrdering.Matching);
        await Assert.That(zebra.Properties.Select(p => p.Name).ToList())
            .IsEquivalentTo(new[] { "Total", "Note", "Qty", "Shade" }, CollectionOrdering.Matching);
        await Assert.That(zebra.MemberOrder.Select(m => $"{m.Kind}:{m.Name}").ToList())
            .IsEquivalentTo(
                new[] { "Property:Total", "Property:Note", "Property:Qty", "Property:Shade", "Navigation:apples" },
                CollectionOrdering.Matching);
        await Assert.That(domain.Types.OfType<EnumType>().Single().MemberNames.ToList())
            .IsEquivalentTo(new[] { "Beta", "Alpha" }, CollectionOrdering.Matching);
        await Assert.That(domain.Types.OfType<ValueType>().Single().Properties.Select(p => p.Name).ToList())
            .IsEquivalentTo(new[] { "Zed", "Abe" }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task DeclarationOrder_HelperMatchesTheSource() {
        var order = DeclarationOrder.Of(OrderFixture);

        await Assert.That(order.Types).IsEquivalentTo(AllTypes, CollectionOrdering.Matching);
        await Assert.That(order.Members("Zebra")).IsEquivalentTo(ZebraMembers, CollectionOrdering.Matching);
        await Assert.That(order.Members("Apple")).IsEquivalentTo(AppleMembers, CollectionOrdering.Matching);
        await Assert.That(order.Navigations("Zebra")).IsEquivalentTo(new[] { "apples" }, CollectionOrdering.Matching);

        // A bare navigation (third token a type name) reports as a property; subscriptions and
        // unnamed members surface in the outline but are not entity members.
        var caveats = DeclarationOrder.Of("""
            domain Caveats
            Owner: entity { Name: Text }
            Thing: entity {
              Owner: Owner
              Total: Number
              when Owner Active { }
            }
            """);
        await Assert.That(caveats.Properties("Thing")).Contains("Owner");
        await Assert.That(caveats.Navigations("Thing")).IsEmpty();
        await Assert.That(caveats.Raw("Thing").Count).IsGreaterThan(caveats.Members("Thing").Count);
    }

    [Test]
    public async Task Markdown_ListsTypesAndMembersInDeclarationOrder() {
        var report = DomainReportExporter.Export(Fixture());

        await Assert.That(report.IndexOf("### Zebra", StringComparison.Ordinal))
            .IsLessThan(report.IndexOf("### Apple", StringComparison.Ordinal));
        await Assert.That(RowNames(report, "### Zebra"))
            .IsEquivalentTo(new[] { "Total", "Note", "Qty", "Shade" }, CollectionOrdering.Matching);
        await Assert.That(RowNames(report, "### Money"))
            .IsEquivalentTo(new[] { "Zed", "Abe" }, CollectionOrdering.Matching);
        await Assert.That(RowNames(report, "### Flavour"))
            .IsEquivalentTo(new[] { "Beta", "Alpha" }, CollectionOrdering.Matching);
        await Assert.That(RelationshipRows(report))
            .IsEquivalentTo(new[] { "Zebra|apples", "Apple|parent" }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task Html_ListsTypesAndMembersInDeclarationOrder() {
        var html = DomainHtmlExporter.Export(Fixture());

        await Assert.That(html.IndexOf("id=\"entity-Zebra\"", StringComparison.Ordinal))
            .IsLessThan(html.IndexOf("id=\"entity-Apple\"", StringComparison.Ordinal));
        await Assert.That(Cells(html, "entity-Zebra"))
            .IsEquivalentTo(new[] { "Total", "Note", "Qty", "Shade" }, CollectionOrdering.Matching);
        await Assert.That(Cells(html, "value-Money"))
            .IsEquivalentTo(new[] { "Zed", "Abe" }, CollectionOrdering.Matching);
        await Assert.That(RelationshipCells(html))
            .IsEquivalentTo(new[] { "Zebra|apples", "Apple|parent" }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task JsonSchema_ListsTypesAndMembersInDeclarationOrder() {
        var defs = JsonNode.Parse(JsonSchemaExporter.Export(Fixture()))!["$defs"]!;

        await Assert.That(Keys(defs)).IsEquivalentTo(AllTypes, CollectionOrdering.Matching);
        await Assert.That(Keys(defs["Zebra"]!["properties"])).IsEquivalentTo(ZebraMembers, CollectionOrdering.Matching);
        await Assert.That(Keys(defs["Money"]!["properties"]))
            .IsEquivalentTo(new[] { "Zed", "Abe" }, CollectionOrdering.Matching);
        await Assert.That(defs["Flavour"]!["enum"]!.AsArray().Select(v => v!.GetValue<string>()).ToList())
            .IsEquivalentTo(new[] { "Beta", "Alpha" }, CollectionOrdering.Matching);
        await Assert.That(Keys(defs["Flavour"]!["x-poly-enum"]))
            .IsEquivalentTo(new[] { "Beta", "Alpha" }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task JsonLd_ListsTypesAndMembersInDeclarationOrder() {
        var json = JsonLdExporter.Export(Fixture());

        await Assert.That(LdClassNames(json)).IsEquivalentTo(AllTypes, CollectionOrdering.Matching);
        await Assert.That(LdPropertyNames(json, "Money"))
            .IsEquivalentTo(new[] { "Zed", "Abe" }, CollectionOrdering.Matching);
        await Assert.That(LdPropertyNames(json, "Zebra")).IsEquivalentTo(ZebraMembers, CollectionOrdering.Matching);
        await Assert.That(LdEnumMemberIds(json))
            .IsEquivalentTo(new[] { "beta", "alpha" }, CollectionOrdering.Matching);
        await Assert.That(Keys(JsonNode.Parse(json)!["@context"])).IsEquivalentTo(
            new[] {
                "rdf", "rdfs", "xsd", "Flavour", "Money", "Zebra", "Apple",
                "Beta", "Alpha", "Zed", "Abe", "Total", "Zebra.Note", "Zebra.Qty", "Shade", "apples",
                "Apple.Qty", "Apple.Note", "parent",
            },
            CollectionOrdering.Matching);
    }

    [Test]
    public async Task Shacl_ListsTypesAndMembersInDeclarationOrder() {
        var json = ShaclExporter.Export(Fixture());

        await Assert.That(ShapeNames(json))
            .IsEquivalentTo(new[] { "Money", "Zebra", "Apple" }, CollectionOrdering.Matching);
        await Assert.That(ShapePaths(json, "Zebra")).IsEquivalentTo(ZebraMembers, CollectionOrdering.Matching);
        await Assert.That(ShapePaths(json, "Money"))
            .IsEquivalentTo(new[] { "Zed", "Abe" }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task EveryProjection_ListsTypesAndMembersInDeclarationOrder() {
        var domain = Fixture();
        var order = DeclarationOrder.Of(OrderFixture);
        var entityTypes = order.Types.Where(t => t is "Zebra" or "Apple").ToList();
        var nonEnumTypes = order.Types.Where(t => t != "Flavour").ToList();

        var markdown = DomainReportExporter.Export(domain);
        var html = DomainHtmlExporter.Export(domain);
        var schema = JsonNode.Parse(JsonSchemaExporter.Export(domain))!["$defs"]!;
        var jsonLd = JsonLdExporter.Export(domain);
        var shacl = ShaclExporter.Export(domain);

        // Types, compared by name against the source order.
        await Assert.That(Headings(markdown, "## Entities")).IsEquivalentTo(entityTypes, CollectionOrdering.Matching);
        await Assert.That(SectionIds(html).Where(t => t is "Zebra" or "Apple").ToList())
            .IsEquivalentTo(entityTypes, CollectionOrdering.Matching);
        await Assert.That(Keys(schema)).IsEquivalentTo(order.Types, CollectionOrdering.Matching);
        await Assert.That(LdClassNames(jsonLd)).IsEquivalentTo(order.Types, CollectionOrdering.Matching);
        await Assert.That(ShapeNames(shacl)).IsEquivalentTo(nonEnumTypes, CollectionOrdering.Matching);

        // Members, by name. Tables list properties and the relationships table lists navigations.
        foreach (var entity in entityTypes) {
            var properties = order.Properties(entity);
            var navigations = order.Navigations(entity);
            var all = order.Members(entity);

            await Assert.That(RowNames(markdown, $"### {entity}")).IsEquivalentTo(properties, CollectionOrdering.Matching);
            await Assert.That(Cells(html, $"entity-{entity}")).IsEquivalentTo(properties, CollectionOrdering.Matching);
            await Assert.That(Keys(schema[entity]!["properties"])).IsEquivalentTo(all, CollectionOrdering.Matching);
            await Assert.That(LdPropertyNames(jsonLd, entity)).IsEquivalentTo(all, CollectionOrdering.Matching);
            await Assert.That(ShapePaths(shacl, entity)).IsEquivalentTo(all, CollectionOrdering.Matching);
            await Assert.That(RelationshipRows(markdown).Where(r => r.StartsWith(entity + "|", StringComparison.Ordinal))
                .Select(r => r[(entity.Length + 1)..]).ToList())
                .IsEquivalentTo(navigations, CollectionOrdering.Matching);
        }

        // The outline cannot see enum members or value-type properties: literals.
        await Assert.That(RowNames(markdown, "### Flavour"))
            .IsEquivalentTo(new[] { "Beta", "Alpha" }, CollectionOrdering.Matching);
        await Assert.That(RowNames(markdown, "### Money"))
            .IsEquivalentTo(new[] { "Zed", "Abe" }, CollectionOrdering.Matching);
        await Assert.That(Cells(html, "value-Money"))
            .IsEquivalentTo(new[] { "Zed", "Abe" }, CollectionOrdering.Matching);
        await Assert.That(Keys(schema["Money"]!["properties"]))
            .IsEquivalentTo(new[] { "Zed", "Abe" }, CollectionOrdering.Matching);
        await Assert.That(Keys(schema["Flavour"]!["x-poly-enum"]))
            .IsEquivalentTo(new[] { "Beta", "Alpha" }, CollectionOrdering.Matching);
        await Assert.That(LdPropertyNames(jsonLd, "Money"))
            .IsEquivalentTo(new[] { "Zed", "Abe" }, CollectionOrdering.Matching);
        await Assert.That(ShapePaths(shacl, "Money"))
            .IsEquivalentTo(new[] { "Zed", "Abe" }, CollectionOrdering.Matching);
    }

    /// <summary>
    /// D-01 carry: a navigation declared between two properties stays between them in every
    /// property-and-navigation projection. Fails if <c>EntityDeclarationOrder.Members</c> stops
    /// walking <c>MemberOrder</c>, because the per-list fallback is properties then navigations.
    /// </summary>
    [Test]
    public async Task Projections_NavigationBetweenProperties_StaysInterleaved() {
        var domain = EvolvedDomain.FromDsl("""
            domain Shop
            Line: entity {
              Qty: Number
            }
            Order: entity {
              Total: Number
              lines: many Line
              Note: Text
            }
            """).Domain;
        string[] expected = ["Total", "lines", "Note"];

        var schema = JsonNode.Parse(JsonSchemaExporter.Export(domain))!["$defs"]!;
        var jsonLd = JsonLdExporter.Export(domain);
        var shacl = ShaclExporter.Export(domain);

        await Assert.That(Keys(schema["Order"]!["properties"])).IsEquivalentTo(expected, CollectionOrdering.Matching);
        await Assert.That(LdPropertyNames(jsonLd, "Order")).IsEquivalentTo(expected, CollectionOrdering.Matching);
        await Assert.That(ShapePaths(shacl, "Order")).IsEquivalentTo(expected, CollectionOrdering.Matching);
    }
}