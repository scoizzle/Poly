using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;
using Poly.Packs.Product;

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

    /// <summary>P-11b (#687): a create with a Required link sends the target's key, as the sample key its own route uses; a
    /// target with a shadow key has no key a body could carry, so its dependant has no sample request at all.</summary>
    [Test]
    public async Task CreateSample_RequiredLink_CarriesTheTargetsKey() {
        var http = Render(ParseDomain("""
            domain Uni
            Course: entity { Code: Text unique  Title: Text }
            Section: entity { Cap: Number  Code: Text unique  course: Course }
            Shelf: entity { Slot: Number unique }
            Book: entity { Title: Text  shelf: Shelf }
            Customer: entity { Name: Text }
            Order: entity { Number: Text  customer: Customer }
            """)).Replace("\r\n", "\n");

        await Assert.That(http).Contains("### Create a new section");
        await Assert.That(http).Contains("\"Code\": \"example-code\",\n    \"CourseId\": \"example-code\"\n}");
        await Assert.That(http).Contains("\"Title\": \"sample\",\n    \"ShelfId\": 0\n}");
        await Assert.That(http).Contains("### Create a new customer");
        await Assert.That(http).DoesNotContain("### Create a new order");
    }

    /// <summary>P-11b: the link member carries exactly the value the target's own create sample posts for its key, for a
    /// Text, a Number and a Date key, so the samples succeed in file order.</summary>
    [Test]
    public async Task CreateSample_RequiredLink_SendsTheValueTheTargetsCreateSamplePosts() {
        var http = Render(ParseDomain("""
            domain Uni
            uses temporal
            Course: entity { Code: Text unique }
            Section: entity { Code: Text unique  course: Course }
            Shelf: entity { Slot: Number unique }
            Book: entity { Title: Text  shelf: Shelf }
            Day: entity { On: Date unique }
            Event: entity { Title: Text  day: Day }
            """));

        static string Member(string text, string name) =>
            System.Text.RegularExpressions.Regex.Match(text, "\"" + name + "\": ([^,\r\n]+)").Groups[1].Value;
        await Assert.That(Member(http, "CourseId")).IsEqualTo(Member(http, "Code")).Because(http);
        await Assert.That(Member(http, "ShelfId")).IsEqualTo(Member(http, "Slot")).Because(http);
        await Assert.That(Member(http, "DayId")).IsEqualTo(Member(http, "On")).Because(http);
        await Assert.That(Member(http, "DayId")).IsNotEqualTo("\"example\"");
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

    /// <summary>#596: a sample request the domain itself rejects teaches the reader nothing. Every value the
    /// create sample posts for a constrained property must satisfy that property's declared range, length and
    /// pattern, so the sample a new user sends first answers 201, not 400.</summary>
    [Test]
    public async Task CreateSample_SatisfiesTheDomainsOwnConstraints_Issue596() {
        var domain = ParseDomain("""
            domain Inventory
            Category: enum { Tools, Toys, Food }
            Product: entity {
              Sku: Text unique length(3, 12) pattern("^[A-Z0-9]+$")
              Name: Text length(1, 50)
              Price: Number range(0, 10000)
              Stock: Number range(0, 100000) default(0)
              Cat: Category default(Tools)
              Note: Text optional length(0, 100)
            }
            Customer: entity {
              Email: Text unique length(5, 80) pattern("^[^@]+@[^@]+$")
              Name: Text length(1, 50)
              orders: many Order
            }
            Order: entity {
              Qty: Number range(1, 100)
              Total: Number range(0, 1000000)
              Reject: action (Why: Text) { }
            }
            """);
        var http = Render(domain).Replace("\r\n", "\n");

        var checkedValues = 0;
        foreach (var entity in domain.Types.OfType<Entity>()) {
            var body = CreateSampleBody(http, entity);
            if (body is null) continue;
            foreach (var prop in entity.Properties) {
                if (!body.TryGetValue(prop.Name, out var json)) continue;
                checkedValues++;
                await AssertSatisfies(prop, json, $"{entity.Name}.{prop.Name} in\n{http}");
            }
        }

        // The sample set above carries four constrained scalars plus a pattern on two of them; if the
        // parse misses the bodies, the loop would pass vacuously.
        await Assert.That(checkedValues).IsGreaterThanOrEqualTo(6);
    }

    private static Dictionary<string, string>? CreateSampleBody(string http, Entity entity) {
        var header = $"### Create a new {DomainTypeMapping.ToCamelCase(entity.Name)}";
        var start = http.IndexOf(header + "\n", StringComparison.Ordinal);
        if (start < 0) return null;
        var open = http.IndexOf("\n{\n", start, StringComparison.Ordinal);
        if (open < 0) return null;
        var close = http.IndexOf("\n}\n", open, StringComparison.Ordinal);
        if (close < 0) return null;
        var members = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in http[(open + 3)..close].Split('\n', StringSplitOptions.RemoveEmptyEntries)) {
            var colon = line.IndexOf("\": ", StringComparison.Ordinal);
            if (colon < 0) continue;
            var name = line[..colon].Trim().TrimStart('"');
            var value = line[(colon + 3)..].Trim().TrimEnd(',');
            members[name] = value;
        }
        return members;
    }

    private static async Task AssertSatisfies(Property prop, string json, string because) {
        foreach (var constraint in prop.Constraints) {
            switch (constraint) {
                case RangeConstraint range: {
                        var value = decimal.Parse(json, System.Globalization.CultureInfo.InvariantCulture);
                        if (range.Minimum is { } min)
                            await Assert.That(value).IsGreaterThanOrEqualTo(Convert.ToDecimal(min, System.Globalization.CultureInfo.InvariantCulture)).Because(because);
                        if (range.Maximum is { } max)
                            await Assert.That(value).IsLessThanOrEqualTo(Convert.ToDecimal(max, System.Globalization.CultureInfo.InvariantCulture)).Because(because);
                        break;
                    }
                case LengthConstraint length: {
                        var text = json.Trim('"');
                        await Assert.That(text.Length).IsGreaterThanOrEqualTo(length.MinLength).Because(because);
                        await Assert.That(text.Length).IsLessThanOrEqualTo(length.MaxLength).Because(because);
                        break;
                    }
                case PatternConstraint pattern:
                    await Assert.That(System.Text.RegularExpressions.Regex.IsMatch(json.Trim('"'), pattern.Pattern))
                        .IsTrue().Because($"{because}: {json} vs {pattern.Pattern}");
                    break;
            }
        }
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