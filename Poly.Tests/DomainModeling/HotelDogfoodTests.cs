using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;
using Poly.Interpretation.CSharp;

namespace Poly.Tests.DomainModeling;

/// <summary>
/// Hotel dogfood: guest-room stay + folio, property meeting halls that
/// subdivide into sections, and airwall occupancy (whole vs partition).
/// </summary>
public class HotelDogfoodTests {
    private static string PolyText() {
        var root = FindRepoRoot();
        return File.ReadAllText(Path.Combine(root, "docs/probes/dogfood/hotel.poly"));
    }

    private static (Domain Domain, AnalysisResult Analysis) Evolve() {
        var changes = new PolyDslParser(PolyText()).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(changes);
        if (!result.Succeeded) {
            var errors = string.Join("; ", result.Analysis.Diagnostics
                .Where(d => d.Severity == Poly.Analysis.DiagnosticSeverity.Error)
                .Select(d => d.Message));
            throw new InvalidOperationException($"Evolution failed: {errors}");
        }
        var analysis = DomainModelAnalyzer.Analyze(result.Root!);
        if (analysis.HasErrors) {
            var errors = string.Join("; ", analysis.Diagnostics
                .Where(d => d.Severity == Poly.Analysis.DiagnosticSeverity.Error)
                .Select(d => d.Message));
            throw new InvalidOperationException($"Analysis failed: {errors}");
        }
        return (result.Root!, analysis);
    }

    private static string FindRepoRoot() {
        var dir = AppContext.BaseDirectory;
        while (dir is not null) {
            if (File.Exists(Path.Combine(dir, "Poly.sln"))
                || File.Exists(Path.Combine(dir, "docs/CORE.md")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new InvalidOperationException("Could not find repo root from " + AppContext.BaseDirectory);
    }

    [Test]
    public async Task Hotel_Export_Compiles() {
        var (domain, analysis) = Evolve();
        var types = DomainProgramProjection.ToSyntax(domain, analysis);
        var cs = new CSharpGenerator().Generate(types);

        await Assert.That(cs).DoesNotContain("void Notify(string stageName)");
        await Assert.That(cs).Contains("IEnumerable<Reservation>? reservations = default");
        await Assert.That(cs).Contains("IEnumerable<Section>? sections = default");
        await Assert.That(cs).Contains("WhenEachReservationDeparted");
        await Assert.That(cs).Contains("WhenEachEventHoldReleased");

        var tree = CSharpSyntaxTree.ParseText("#nullable enable\n" + cs);
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
            ?.Split(Path.PathSeparator)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToArray() ?? [];
        var compilation = CSharpCompilation.Create(
            "HotelExport",
            [tree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var errors = compilation.GetDiagnostics()
            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .ToArray();
        await Assert.That(errors).IsEmpty();
    }

    [Test]
    public async Task Hotel_Runtime_WalksStayFolioAndMeetingAirwall() {
        var (domain, _) = Evolve();
        var exec = ModuleExec.For(domain);
        Entity E(string n) => domain.Types.OfType<Entity>().First(t => t.Name == n);

        var roomType = exec.Create("RoomType", new Dictionary<string, object?> {
            ["Name"] = "Deluxe",
            ["NightlyRate"] = 220m,
            ["MaxOccupancy"] = 3L
        }).Instance!;
        var room = exec.Create("Room", new Dictionary<string, object?> {
            ["Number"] = "101",
            ["Floor"] = 1L,
            ["NightlyRate"] = 220m,
            ["MaxOccupancy"] = 3L
        }).Instance!;
        var guest = exec.Create("Guest", new Dictionary<string, object?> {
            ["Name"] = "Alice Chen",
            ["Email"] = "alice@example.com",
            ["Phone"] = "55501001234"
        }).Instance!;
        exec.Link("roomType", room, roomType);

        var blockedBook = exec.Invoke(guest, "BookStay");
        await Assert.That(blockedBook.Succeeded).IsFalse();

        var stay = exec.Invoke(guest, "BookStay", new Dictionary<string, object?> {
            ["room"] = room,
            ["party"] = 2L,
            ["nights"] = 2L,
            ["arrive"] = new DateOnly(2026, 9, 10),
            ["depart"] = new DateOnly(2026, 9, 12)
        });
        await Assert.That(stay.Succeeded).IsTrue();
        var reservation = stay.Instance!;
        await Assert.That(ModuleReads.Stage(reservation)).IsEqualTo("Requested");
        await Assert.That(reservation.Bag["NightlyRate"]).IsEqualTo(220m);
        await Assert.That(exec.EvaluatePolicy(guest, (E("Guest").Policies.First(p => p.Name == "HasStays")).Name)).IsTrue();

        var oversized = exec.Invoke(guest, "BookStay", new Dictionary<string, object?> {
            ["room"] = room,
            ["party"] = 8L,
            ["nights"] = 1L,
            ["arrive"] = new DateOnly(2026, 9, 15),
            ["depart"] = new DateOnly(2026, 9, 16)
        });
        await Assert.That(oversized.Succeeded).IsTrue();
        var tooBig = oversized.Instance!;
        var confirmTooBig = exec.Invoke(tooBig, "Confirm");
        await Assert.That(confirmTooBig.Succeeded).IsFalse();
        await Assert.That(confirmTooBig.Error).Contains("PartyFits");

        await Assert.That(exec.Invoke(reservation, "Confirm").Succeeded).IsTrue();
        await Assert.That(ModuleReads.Stage(reservation)).IsEqualTo("Confirmed");
        await Assert.That(exec.Invoke(reservation, "CheckIn").Succeeded).IsTrue();
        await Assert.That(ModuleReads.Stage(reservation)).IsEqualTo("InHouse");
        await Assert.That((bool)room.Bag["Occupied"]!).IsTrue();

        var offlineBusy = exec.Invoke(room, "TakeOutOfService");
        await Assert.That(offlineBusy.Succeeded).IsFalse();
        await Assert.That(offlineBusy.Error).Contains("IsVacant");

        await Assert.That(exec.Invoke(reservation, "PostStayCharge").Succeeded).IsTrue();
        var folio = ModuleReads.Related(exec, "folio", reservation).Single();
        await Assert.That(folio.Bag["Balance"]).IsEqualTo(220m);

        var unpaid = exec.Invoke(reservation, "CheckOut");
        await Assert.That(unpaid.Succeeded).IsFalse();
        await Assert.That(unpaid.Error).Contains("BillSettled");

        await Assert.That(exec.Invoke(folio, "Settle").Succeeded).IsTrue();
        await Assert.That(exec.Invoke(reservation, "CheckOut").Succeeded).IsTrue();
        await Assert.That(ModuleReads.Stage(reservation)).IsEqualTo("Departed");
        await Assert.That((bool)room.Bag["Occupied"]!).IsFalse();
        await Assert.That((bool)room.Bag["Dirty"]!).IsTrue();
        await Assert.That(guest.Bag["StayCount"]).IsEqualTo(1L);
        await Assert.That(guest.Bag["NightCount"]).IsEqualTo(2L);
        await Assert.That(exec.EvaluatePolicy(room, (E("Room").Policies.First(p => p.Name == "IsReady")).Name)).IsFalse();
        await Assert.That(exec.Invoke(room, "Inspect").Succeeded).IsTrue();
        await Assert.That(exec.EvaluatePolicy(room, (E("Room").Policies.First(p => p.Name == "IsReady")).Name)).IsTrue();

        var bob = exec.Create("Guest", new Dictionary<string, object?> {
            ["Name"] = "Bob Ruiz",
            ["Email"] = "bob@example.com",
            ["Phone"] = "55501998877"
        }).Instance!;
        await Assert.That(exec.Invoke(bob, "Block").Succeeded).IsTrue();
        var blockedStay = exec.Invoke(bob, "BookStay", new Dictionary<string, object?> {
            ["room"] = room,
            ["party"] = 1L,
            ["nights"] = 1L,
            ["arrive"] = new DateOnly(2026, 9, 20),
            ["depart"] = new DateOnly(2026, 9, 21)
        });
        await Assert.That(blockedStay.Succeeded).IsFalse();

        var property = exec.Create("Property", new Dictionary<string, object?> {
            ["Name"] = "Harbor Inn",
            ["City"] = "Seattle"
        }).Instance!;
        var addHall = exec.Invoke(property, "AddHall", new Dictionary<string, object?> {
            ["name"] = "Grand Ballroom",
            ["seats"] = 300L,
            ["rate"] = 4000L
        });
        await Assert.That(addHall.Succeeded).IsTrue();
        var hall = addHall.Instance!;
        await Assert.That(exec.EvaluatePolicy(property, (E("Property").Policies.First(p => p.Name == "HasHalls")).Name)).IsTrue();

        var salonA = exec.Create("Section", new Dictionary<string, object?> {
            ["Name"] = "Salon A",
            ["Capacity"] = 100L,
            ["DayRate"] = 1500m,
        }).Instance!;
        var salonB = exec.Create("Section", new Dictionary<string, object?> {
            ["Name"] = "Salon B",
            ["Capacity"] = 100L,
            ["DayRate"] = 1500m,
        }).Instance!;
        var salonC = exec.Create("Section", new Dictionary<string, object?> {
            ["Name"] = "Salon C",
            ["Capacity"] = 100L,
            ["DayRate"] = 1500m,
        }).Instance!;
        exec.Link("sections", hall, salonA);
        exec.Link("hall", salonA, hall);
        exec.Link("sections", hall, salonB);
        exec.Link("hall", salonB, hall);
        exec.Link("sections", hall, salonC);
        exec.Link("hall", salonC, hall);

        var sectionHold = exec.Invoke(guest, "BookSection", new Dictionary<string, object?> {
            ["section"] = salonA,
            ["hall"] = hall,
            ["party"] = 80L,
            ["arrive"] = new DateOnly(2026, 9, 20),
            ["depart"] = new DateOnly(2026, 9, 21)
        });
        await Assert.That(sectionHold.Succeeded).IsTrue();
        var holdA = sectionHold.Instance!;
        await Assert.That(exec.Invoke(holdA, "Confirm").Succeeded).IsTrue();
        var startAsWhole = exec.Invoke(holdA, "StartHall");
        await Assert.That(startAsWhole.Succeeded).IsFalse();
        await Assert.That(startAsWhole.FailedGuards.SequenceEqual(["HasSection"])).IsTrue();
        await Assert.That(exec.Invoke(holdA, "StartSection").Succeeded).IsTrue();
        await Assert.That((bool)salonA.Bag["Occupied"]!).IsTrue();
        await Assert.That(hall.Bag["BusySections"]).IsEqualTo(1L);
        await Assert.That(exec.EvaluatePolicy(hall, (E("Hall").Policies.First(p => p.Name == "CanBook")).Name)).IsFalse();

        await Assert.That(exec.Invoke(salonB, "Occupy").Succeeded).IsTrue();
        await Assert.That(hall.Bag["BusySections"]).IsEqualTo(2L);

        var wholeHold = exec.Invoke(guest, "BookHall", new Dictionary<string, object?> {
            ["hall"] = hall,
            ["party"] = 250L,
            ["arrive"] = new DateOnly(2026, 9, 22),
            ["depart"] = new DateOnly(2026, 9, 23)
        });
        await Assert.That(wholeHold.Succeeded).IsTrue();
        var holdHall = wholeHold.Instance!;
        await Assert.That(exec.Invoke(holdHall, "Confirm").Succeeded).IsTrue();
        var startBusy = exec.Invoke(holdHall, "StartHall");
        await Assert.That(startBusy.Succeeded).IsFalse();
        await Assert.That(startBusy.Error).Contains("CanBook");

        await Assert.That(exec.Invoke(holdA, "ReleaseSection").Succeeded).IsTrue();
        await Assert.That(exec.Invoke(salonB, "Vacate").Succeeded).IsTrue();
        await Assert.That(hall.Bag["BusySections"]).IsEqualTo(0L);
        await Assert.That(guest.Bag["EventCount"]).IsEqualTo(1L);

        await Assert.That(exec.Invoke(holdHall, "StartHall").Succeeded).IsTrue();
        await Assert.That((bool)hall.Bag["Occupied"]!).IsTrue();
        var occupyC = exec.Invoke(salonC, "Occupy");
        await Assert.That(occupyC.Succeeded).IsFalse();
        await Assert.That(occupyC.Error).Contains("ParentFree");
    }
}