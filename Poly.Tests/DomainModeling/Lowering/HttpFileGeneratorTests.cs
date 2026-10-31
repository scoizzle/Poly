using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;
using Poly.DslCompiler;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Lowering;

public class HttpFileGeneratorTests {
    private static Domain ParseDomain(string poly) {
        if (!poly.Contains("uses persistence", StringComparison.Ordinal)) {
            var nl = poly.IndexOf('\n');
            poly = nl < 0 ? poly : poly[..(nl + 1)] + "uses persistence\n" + poly[(nl + 1)..];
        }
        var parser = new PolyDslParser(poly, ExtensionCatalog.Core.Authoring);
        var changes = parser.Parse();
        var result = new DomainEvolution(Poly.DomainModeling.DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(changes);
        if (!result.Succeeded) throw new InvalidOperationException("Domain evolution failed");
        return result.Root!;
    }

    private static string Render(Domain domain) {
        var analysis = DomainModelAnalyzer.Analyze(domain);
        var storage = analysis.GetMetadata<StorageMappingMetadata>(domain)?.Storage
            ?? throw new InvalidOperationException("Domain analysis did not produce StorageMappingMetadata.");
        var behavior = BehaviorMetadata.From(domain, analysis);
        var aggregate = analysis.GetMetadata<OwnershipAggregateMetadata>(domain)?.Aggregate
            ?? throw new InvalidOperationException("Domain analysis did not produce OwnershipAggregateMetadata.");
        return new HttpFileGenerator(domain, analysis, storage, behavior, aggregate).Generate();
    }

    [Test]
    public async Task ValueTypeParam_EmitsJsonObject() {
        var http = Render(ParseDomain("""
            domain Library
            Mailer: contract external mailer v1 {
              Message: value {
                To: Text
                Subject: Text
                Body: Text
              }
              Send: outbound operation Message
            }
            Patron: entity {
              Email: Text unique
              NotifyPatron: action (message: Message) { }
            }
            """));
        await Assert.That(http).Contains(
            "\"Message\": { \"to\": \"sample\", \"subject\": \"sample\", \"body\": \"sample\" }");
        await Assert.That(http.Contains("\"message\": 0")).IsFalse();
    }

    [Test]
    public async Task SectionHeader_EnglishPlural() {
        // S19: the demo.http section label and request routes pluralize with English rules.
        var http = Render(ParseDomain("""
            domain Col
            Address: entity {
              Line1: Text
              City: Text
            }
            """));
        await Assert.That(http).Contains("### ──────────── Addresses ────────────");
        await Assert.That(http.Contains("Addresss")).IsFalse();
        await Assert.That(http).Contains("/api/addresses");
    }

    [Test]
    public async Task StageScopedAction_ListedOnce() {
        var http = Render(ParseDomain("""
            domain T
            Book: entity { Isbn: Text unique }
            Patron: entity {
              Email: Text unique
              loans: many Loan
            }
            Loan: entity {
              book: Book
              Active: stage { Return: action { } }
              Overdue: stage { Return: action { } }
            }
            """));
        await Assert.That(CountOf(http, "### Action: Return")).IsEqualTo(1);
        await Assert.That(CountOf(http, "/loans/1/return")).IsEqualTo(1);
    }

    [Test]
    public async Task ActionBody_PascalizesPropertyNames() {
        var http = Render(ParseDomain("""
            domain T
            Account: entity {
              Name: Text unique
              PlaceHq: action (city: Text, region: Text) { }
            }
            """));
        await Assert.That(http).Contains("\"City\"");
        await Assert.That(http).Contains("\"Region\"");
        await Assert.That(http.Contains("\"city\"")).IsFalse();
    }

    private static int CountOf(string text, string value) {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0) {
            count++;
            index += value.Length;
        }
        return count;
    }
}