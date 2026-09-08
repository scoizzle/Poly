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
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(changes);
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
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var cs = new CSharpGenerator().Generate(types);

        await Assert.That(cs).DoesNotContain("void Notify(string stageName)");
        await Assert.That(cs).Contains("IEnumerable<Reservation>? reservations = null");
        await Assert.That(cs).Contains("IEnumerable<Section>? sections = null");
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
        var store = new DomainInstanceStore();
        Entity E(string n) => domain.Types.OfType<Entity>().First(t => t.Name == n);

        var roomType = DomainEntityInstance.Create(E("RoomType"),
            new Dictionary<string, object?> {
                ["Name"] = "Deluxe",
                ["NightlyRate"] = 220L,
                ["MaxOccupancy"] = 3L
            }, domain);
        var room = DomainEntityInstance.Create(E("Room"),
            new Dictionary<string, object?> {
                ["Number"] = "101",
                ["Floor"] = 1L,
                ["NightlyRate"] = 220L,
                ["MaxOccupancy"] = 3L
            }, domain);
        var guest = DomainEntityInstance.Create(E("Guest"),
            new Dictionary<string, object?> {
                ["Name"] = "Alice Chen",
                ["Email"] = "alice@example.com",
                ["Phone"] = "55501001234"
            }, domain);
        store.Add(roomType);
        store.Add(room);
        store.Add(guest);
        store.Link("roomType", room, roomType);

        var blockedBook = guest.InvokeAction("BookStay");
        await Assert.That(blockedBook.Succeeded).IsFalse();

        var stay = guest.InvokeAction("BookStay",
            new Dictionary<string, object?> {
                ["room"] = room,
                ["party"] = 2L,
                ["nights"] = 2L,
                ["arrive"] = new DateOnly(2026, 9, 10),
                ["depart"] = new DateOnly(2026, 9, 12)
            });
        await Assert.That(stay.Succeeded).IsTrue();
        var reservation = stay.ResultInstance!;
        await Assert.That(reservation.CurrentStage).IsEqualTo("Requested");
        await Assert.That(reservation.GetProperty<object>("NightlyRate")).IsEqualTo(220L);
        await Assert.That(guest.EvaluatePolicy(E("Guest").Policies.First(p => p.Name == "HasStays"))).IsTrue();

        var oversized = guest.InvokeAction("BookStay",
            new Dictionary<string, object?> {
                ["room"] = room,
                ["party"] = 8L,
                ["nights"] = 1L,
                ["arrive"] = new DateOnly(2026, 9, 15),
                ["depart"] = new DateOnly(2026, 9, 16)
            });
        await Assert.That(oversized.Succeeded).IsTrue();
        var tooBig = oversized.ResultInstance!;
        var confirmTooBig = tooBig.InvokeAction("Confirm");
        await Assert.That(confirmTooBig.Succeeded).IsFalse();
        await Assert.That(confirmTooBig.FailedGuards).Contains("PartyFits");

        await Assert.That(reservation.InvokeAction("Confirm").Succeeded).IsTrue();
        await Assert.That(reservation.CurrentStage).IsEqualTo("Confirmed");
        await Assert.That(reservation.InvokeAction("CheckIn").Succeeded).IsTrue();
        await Assert.That(reservation.CurrentStage).IsEqualTo("InHouse");
        await Assert.That(room.GetProperty<bool>("Occupied")).IsTrue();

        var offlineBusy = room.InvokeAction("TakeOutOfService");
        await Assert.That(offlineBusy.Succeeded).IsFalse();
        await Assert.That(offlineBusy.FailedGuards).Contains("IsVacant");

        await Assert.That(reservation.InvokeAction("PostStayCharge").Succeeded).IsTrue();
        var folio = store.GetRelatedInstances("folio", reservation).Single();
        await Assert.That(folio.GetProperty<object>("Balance")).IsEqualTo(220L);

        var unpaid = reservation.InvokeAction("CheckOut");
        await Assert.That(unpaid.Succeeded).IsFalse();
        await Assert.That(unpaid.FailedGuards).Contains("BillSettled");

        await Assert.That(folio.InvokeAction("Settle").Succeeded).IsTrue();
        await Assert.That(reservation.InvokeAction("CheckOut").Succeeded).IsTrue();
        await Assert.That(reservation.CurrentStage).IsEqualTo("Departed");
        await Assert.That(room.GetProperty<bool>("Occupied")).IsFalse();
        await Assert.That(room.GetProperty<bool>("Dirty")).IsTrue();
        await Assert.That(guest.GetProperty<object>("StayCount")).IsEqualTo(1L);
        await Assert.That(guest.GetProperty<object>("NightCount")).IsEqualTo(2L);
        await Assert.That(room.EvaluatePolicy(E("Room").Policies.First(p => p.Name == "IsReady"))).IsFalse();
        await Assert.That(room.InvokeAction("Inspect").Succeeded).IsTrue();
        await Assert.That(room.EvaluatePolicy(E("Room").Policies.First(p => p.Name == "IsReady"))).IsTrue();

        var bob = DomainEntityInstance.Create(E("Guest"),
            new Dictionary<string, object?> {
                ["Name"] = "Bob Ruiz",
                ["Email"] = "bob@example.com",
                ["Phone"] = "55501998877"
            }, domain);
        store.Add(bob);
        await Assert.That(bob.InvokeAction("Block").Succeeded).IsTrue();
        var blockedStay = bob.InvokeAction("BookStay",
            new Dictionary<string, object?> {
                ["room"] = room,
                ["party"] = 1L,
                ["nights"] = 1L,
                ["arrive"] = new DateOnly(2026, 9, 20),
                ["depart"] = new DateOnly(2026, 9, 21)
            });
        await Assert.That(blockedStay.Succeeded).IsFalse();

        var property = DomainEntityInstance.Create(E("Property"),
            new Dictionary<string, object?> {
                ["Name"] = "Harbor Inn",
                ["City"] = "Seattle"
            }, domain);
        store.Add(property);
        var addHall = property.InvokeAction("AddHall",
            new Dictionary<string, object?> {
                ["name"] = "Grand Ballroom",
                ["seats"] = 300L,
                ["rate"] = 4000L
            });
        await Assert.That(addHall.Succeeded).IsTrue();
        var hall = addHall.ResultInstance!;
        await Assert.That(property.EvaluatePolicy(E("Property").Policies.First(p => p.Name == "HasHalls"))).IsTrue();

        var salonA = DomainEntityInstance.Create(E("Section"),
            new Dictionary<string, object?> {
                ["Name"] = "Salon A",
                ["Capacity"] = 100L,
                ["DayRate"] = 1500L
            }, domain);
        var salonB = DomainEntityInstance.Create(E("Section"),
            new Dictionary<string, object?> {
                ["Name"] = "Salon B",
                ["Capacity"] = 100L,
                ["DayRate"] = 1500L
            }, domain);
        var salonC = DomainEntityInstance.Create(E("Section"),
            new Dictionary<string, object?> {
                ["Name"] = "Salon C",
                ["Capacity"] = 100L,
                ["DayRate"] = 1500L
            }, domain);
        store.Add(salonA);
        store.Add(salonB);
        store.Add(salonC);
        store.Link("sections", hall, salonA);
        store.Link("hall", salonA, hall);
        store.Link("sections", hall, salonB);
        store.Link("hall", salonB, hall);
        store.Link("sections", hall, salonC);
        store.Link("hall", salonC, hall);

        var sectionHold = guest.InvokeAction("BookSection",
            new Dictionary<string, object?> {
                ["section"] = salonA,
                ["hall"] = hall,
                ["party"] = 80L,
                ["arrive"] = new DateOnly(2026, 9, 20),
                ["depart"] = new DateOnly(2026, 9, 21)
            });
        await Assert.That(sectionHold.Succeeded).IsTrue();
        var holdA = sectionHold.ResultInstance!;
        await Assert.That(holdA.InvokeAction("Confirm").Succeeded).IsTrue();
        var startAsWhole = holdA.InvokeAction("StartHall");
        await Assert.That(startAsWhole.Succeeded).IsFalse();
        await Assert.That(startAsWhole.FailedGuards).Contains("not_HasSection");
        await Assert.That(holdA.InvokeAction("StartSection").Succeeded).IsTrue();
        await Assert.That(salonA.GetProperty<bool>("Occupied")).IsTrue();
        await Assert.That(hall.GetProperty<object>("BusySections")).IsEqualTo(1L);
        await Assert.That(hall.EvaluatePolicy(E("Hall").Policies.First(p => p.Name == "CanBook"))).IsFalse();

        await Assert.That(salonB.InvokeAction("Occupy").Succeeded).IsTrue();
        await Assert.That(hall.GetProperty<object>("BusySections")).IsEqualTo(2L);

        var wholeHold = guest.InvokeAction("BookHall",
            new Dictionary<string, object?> {
                ["hall"] = hall,
                ["party"] = 250L,
                ["arrive"] = new DateOnly(2026, 9, 22),
                ["depart"] = new DateOnly(2026, 9, 23)
            });
        await Assert.That(wholeHold.Succeeded).IsTrue();
        var holdHall = wholeHold.ResultInstance!;
        await Assert.That(holdHall.InvokeAction("Confirm").Succeeded).IsTrue();
        var startBusy = holdHall.InvokeAction("StartHall");
        await Assert.That(startBusy.Succeeded).IsFalse();
        await Assert.That(startBusy.ErrorMessage).Contains("CanBook");

        await Assert.That(holdA.InvokeAction("ReleaseSection").Succeeded).IsTrue();
        await Assert.That(salonB.InvokeAction("Vacate").Succeeded).IsTrue();
        await Assert.That(hall.GetProperty<object>("BusySections")).IsEqualTo(0L);
        await Assert.That(guest.GetProperty<object>("EventCount")).IsEqualTo(1L);

        await Assert.That(holdHall.InvokeAction("StartHall").Succeeded).IsTrue();
        await Assert.That(hall.GetProperty<bool>("Occupied")).IsTrue();
        var occupyC = salonC.InvokeAction("Occupy");
        await Assert.That(occupyC.Succeeded).IsFalse();
        await Assert.That(occupyC.FailedGuards).Contains("ParentFree");
    }
}