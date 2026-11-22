using System.Text.RegularExpressions;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// P-11b (#687, row P-11, the required-link half): the create request carries each Required link as the linked entity's route
/// key (owner ruling 2026-11-14). The host looks it up as an action's entity argument is looked up (404 when it does not
/// exist), passes the loaded entity to the lowered <c>Create</c> and loads what the create reads on it.
/// </summary>
public partial class MinimalApiGeneratorTests {
    private static string RenderHost(string poly) => HostGenerator(CompileForHost(poly)).Generate("Ctx");

    private const string SectionPoly = """
        domain Uni
        Course: entity { Code: Text unique  Title: Text }
        Section: entity { Cap: Number  Code: Text unique  course: Course }
        """;

    [Test]
    public async Task CreateRoute_RequiredLinkToANaturalKeyTarget_CarriesTheKeyLooksItUpAndPassesTheTarget() {
        var host = RenderHost(SectionPoly);

        await Assert.That(host).Contains("""app.MapPost("/api/sections", async (SectionDto dto, Ctx db) => {""");
        await Assert.That(host).Contains("public string? CourseId { get; init; }");
        await Assert.That(host).Contains("var course = (dto.CourseId == null ? null : await db.Courses.FirstOrDefaultAsync(e => e.Code == dto.CourseId));");
        // A given key that names nothing is a 404 naming the target; an absent key is no host check at all: the null
        // reaches Create, which answers Required.
        await Assert.That(host).Contains("if (dto.CourseId != null && course == null)");
        await Assert.That(host).Contains("return StatusProblem(404, \"Not Found\", \"Course not found\");");
        await Assert.That(host).Contains("Uni.Section.Create(db, dto.Cap, dto.Code, course)");
        // Not seeded: the seed passes no link, so the section's create would only fail.
        await Assert.That(Regex.Matches(host, @"Section\.Create\(").Count).IsEqualTo(1);
    }

    [Test]
    public async Task CreateRoute_TwoLinksToOneEntityType_GetTwoMembersAndTwoLocals() {
        var host = RenderHost("""
            domain Flights
            Airport: entity { Code: Text unique  Capacity: Number }
            Route: entity {
              Code: Text unique
              origin: Airport
              destination: Airport
            }
            """);

        await Assert.That(host).Contains("public string? OriginId { get; init; }");
        await Assert.That(host).Contains("public string? DestinationId { get; init; }");
        await Assert.That(host).Contains("var airport = (dto.OriginId == null ? null : await db.Airports.FirstOrDefaultAsync(e => e.Code == dto.OriginId));");
        await Assert.That(host).Contains("var airport2 = (dto.DestinationId == null ? null : await db.Airports.FirstOrDefaultAsync(e => e.Code == dto.DestinationId));");
        await Assert.That(host).Contains("Flights.Route.Create(db, dto.Code, airport, airport2)");
    }

    [Test]
    public async Task CreateRoute_ALinkTargetWithANonStringKey_TypesTheMemberAsTheKey() {
        var host = RenderHost("""
            domain Shelves
            Shelf: entity { Slot: Number unique }
            Book: entity { Title: Text  shelf: Shelf }
            """);

        await Assert.That(host).Contains("public long? ShelfId { get; init; }");
        await Assert.That(host).Contains("var shelf = (dto.ShelfId == null ? null : await db.Shelfs.FirstOrDefaultAsync(e => e.Slot == dto.ShelfId));");
    }

    [Test]
    public async Task CreateRoute_ALinkTargetWithAShadowKey_StillHasNoPostRoute() {
        var host = RenderHost("""
            domain Shop
            Customer: entity { Name: Text }
            Order: entity { Number: Text  customer: Customer }
            """);

        await Assert.That(host).Contains("MapPost(\"/api/customers\"");
        await Assert.That(host).Contains("MapGet(\"/api/orders\"");
        await Assert.That(host).DoesNotContain("MapPost(\"/api/orders\"");
        await Assert.That(host).DoesNotContain("OrderDto");
    }

    [Test]
    public async Task CreateRoute_ALinkMemberThatWouldShareANameWithAScalar_HasNoPostRoute() {
        var host = RenderHost("""
            domain Clash
            Course: entity { Code: Text unique }
            Section: entity { Code: Text unique  CourseId: Text  course: Course }
            """);

        await Assert.That(host).DoesNotContain("MapPost(\"/api/sections\"");
        await Assert.That(host).DoesNotContain("SectionDto");
    }

    [Test]
    public async Task CreateRoute_ReadingTheLinkAtCreate_LoadsWhatTheCreateReadsFromTheReadTree() {
        var host = RenderHost("""
            domain Chain
            Region: entity { Code: Text unique  Name: Text }
            Course: entity {
              Code: Text unique
              region: owned Region optional
              MakeRegion: action -> Region { create in region { Code: "r1"  Name: "North" } }
            }
            Section: entity {
              Code: Text unique
              Label: Text optional
              course: Course
              Open: stage { entry { assign Label to course region Name } }
            }
            """);

        await Assert.That(host).Contains("await db.Entry(course).Reference(e => e.Region).LoadAsync();");
        // Within the create route the load sits after its 404 check and before the call, on a non-null target only. The
        // route is sliced out first: the same load now also appears in a GET handler, which is a different statement.
        var create = HandlerOf(host, "MapPost", "/api/sections");
        await Assert.That(create.IndexOf("StatusProblem(404, \"Not Found\", \"Course not found\")", StringComparison.Ordinal))
            .IsLessThan(create.IndexOf("await db.Entry(course).Reference(e => e.Region)", StringComparison.Ordinal));
        await Assert.That(create.IndexOf("await db.Entry(course).Reference(e => e.Region)", StringComparison.Ordinal))
            .IsLessThan(create.IndexOf("Chain.Section.Create(", StringComparison.Ordinal));
    }

    /// <summary>#592: the '/' guard reads a create DTO member, and an optional unique key is not one. Emitting
    /// <c>dto.Tag</c> for it would not compile.</summary>
    [Test]
    public async Task CreateRoute_OptionalUniqueTextKey_HasNoSlashGuardMember() {
        var host = RenderHost("""
            domain Lab
            Slot: entity { Name: Text  Tag: Text unique optional }
            """);

        await Assert.That(host).Contains("app.MapPost(\"/api/slots\"");
        await Assert.That(host).DoesNotContain("dto.Tag");
    }

    /// <summary>#592: a required unique Text key is a DTO member, so its create refuses a '/' before the save.</summary>
    [Test]
    public async Task CreateRoute_RequiredUniqueTextKey_RefusesSlashBeforeTheSave() {
        var host = RenderHost("""
            domain Lab
            Slot: entity { Name: Text  Tag: Text unique }
            """);

        await Assert.That(host).Contains("if (dto.Tag != null && dto.Tag.Contains(\"/\"))");
        await Assert.That(host.IndexOf("dto.Tag.Contains(\"/\")", StringComparison.Ordinal))
            .IsLessThan(host.IndexOf("Lab.Slot.Create(", StringComparison.Ordinal));
    }
}