using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;
using Poly.Interpretation.CSharp;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// Item 1: path-prefix under require → DomainResult.Failure (not throw).
/// </summary>
public class PathPrefixRequireFailureTests {
    [Test]
    public async Task CheckIn_UnlinkedRoom_ReturnsFailure_DoesNotThrow() {
        var (domain, _) = Evolve("""
            domain HotelSlice
            Room: entity {
              Occupied: Boolean
            }
            Reservation: entity {
              room: Room
              RoomFree: policy { room Occupied is false }
              Booked: stage {
                CheckIn: action require RoomFree {
                  transition to InHouse
                }
              }
              InHouse: stage { }
            }
            """);
        var reservation = domain.Types.OfType<Entity>().First(e => e.Name == "Reservation");
        var store = new DomainInstanceStore();
        var res = DomainEntityInstance.Create(reservation, domain: domain);
        store.Add(res);

        ActionInvocationResult? result = null;
        var threw = false;
        try {
            result = res.InvokeAction("CheckIn");
        }
        catch (Exception) {
            threw = true;
        }

        await Assert.That(threw).IsFalse();
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Succeeded).IsFalse();
        await Assert.That(result.ErrorMessage).Contains("requires a linked 'room'");
        await Assert.That(result.FailedGuards).Contains("RoomFree");
    }

    [Test]
    public async Task Export_CheckInRequire_PrintsDomainResultFailure_NotThrow() {
        var (domain, analysis) = Evolve("""
            domain HotelSlice
            Room: entity {
              Occupied: Boolean
            }
            Reservation: entity {
              room: Room
              RoomFree: policy { room Occupied is false }
              Booked: stage {
                CheckIn: action require RoomFree {
                  transition to InHouse
                }
              }
              InHouse: stage { }
            }
            """);
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var unit = new CompilationUnitNode([], null, types, null);
        var cs = new CSharpGenerator().Generate(unit);

        await Assert.That(cs).Contains(
            "DomainResult.Failure(\"'CheckIn' requires a linked 'room' on entity 'Reservation'.\")");
        await Assert.That(cs).DoesNotContain("?? throw new InvalidOperationException");
        await Assert.That(cs).Contains("this.Room!");
    }


    [Test]
    public async Task Confirm_RequireNot_UnlinkedSection_FailsClosed_DoesNotThrow() {
        // F1: require not + unlinked must not invert soft-false to allow the action.
        var (domain, _) = Evolve("""
            domain EnrollSlice
            Section: entity {
              SeatsTaken: Number default(0)
            }
            Enrollment: entity {
              section: Section
              SectionFull: policy { section SeatsTaken >= 1 }
              Pending: stage {
                Confirm: action require not SectionFull {
                  transition to Confirmed
                }
              }
              Confirmed: stage { }
            }
            """);
        var enrollment = domain.Types.OfType<Entity>().First(e => e.Name == "Enrollment");
        var store = new DomainInstanceStore();
        var en = DomainEntityInstance.Create(enrollment, domain: domain);
        store.Add(en);

        ActionInvocationResult? result = null;
        var threw = false;
        try {
            result = en.InvokeAction("Confirm");
        }
        catch (Exception) {
            threw = true;
        }

        await Assert.That(threw).IsFalse();
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Succeeded).IsFalse();
        await Assert.That(result.ErrorMessage).Contains("requires a linked 'section'");
        await Assert.That(result.FailedGuards).Contains("not_SectionFull");
    }


    [Test]
    public async Task Escalate_MultiHop_UnlinkedReporter_FailsClosed() {
        var (domain, _) = Evolve("""
            domain IssueSlice
            Engineer: entity { team: Team }
            Team: entity { TeamName: Text }
            Issue: entity {
              reporter: Engineer
              FromBlueTeam: policy { reporter team TeamName is "Blue" }
              Open: stage {
                Escalate: action require FromBlueTeam { transition to Closed }
              }
              Closed: stage { }
            }
            """);
        var issueE = domain.Types.OfType<Entity>().First(e => e.Name == "Issue");
        var store = new DomainInstanceStore();
        var issue = DomainEntityInstance.Create(issueE, domain: domain);
        store.Add(issue);

        var result = issue.InvokeAction("Escalate");
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.ErrorMessage).Contains("requires a linked 'reporter'");
        await Assert.That(result.FailedGuards).Contains("FromBlueTeam");
    }


    [Test]
    public async Task EmptyBody_Require_PolicyFalse_FailsClosed() {
        // F9: empty-effect require must still run module Failure (not Ok).
        var (domain, _) = Evolve("""
            domain EmptyRequire
            Device: entity {
              Active: Boolean
              IsActive: policy { Active is true }
              Submit: action require IsActive { }
            }
            """);
        var deviceE = domain.Types.OfType<Entity>().First(e => e.Name == "Device");
        var store = new DomainInstanceStore();
        var device = DomainEntityInstance.Create(deviceE,
            new Dictionary<string, object?> { ["Active"] = false }, domain: domain);
        store.Add(device);

        var result = device.InvokeAction("Submit");
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailedGuards).Contains("IsActive");
    }

    [Test]
    public async Task EmptyBody_RequireNot_Unlinked_FailsClosed() {
        // F9 + F1 sibling: empty require not + unlinked path-prefix.
        var (domain, _) = Evolve("""
            domain EmptyRequireNot
            Section: entity {
              SeatsTaken: Number default(0)
            }
            Enrollment: entity {
              section: Section
              SectionFull: policy { section SeatsTaken >= 1 }
              Confirm: action require not SectionFull { }
            }
            """);
        var enrollE = domain.Types.OfType<Entity>().First(e => e.Name == "Enrollment");
        var store = new DomainInstanceStore();
        var en = DomainEntityInstance.Create(enrollE, domain: domain);
        store.Add(en);

        var result = en.InvokeAction("Confirm");
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.ErrorMessage).Contains("requires a linked 'section'");
        await Assert.That(result.FailedGuards).Contains("not_SectionFull");
    }

    private static (Domain Domain, AnalysisResult Analysis) Evolve(string poly) {
        var changes = new PolyDslParser(poly).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(changes);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ",
                result.Analysis.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.Message)));
        var analysis = DomainModelAnalyzer.Analyze(result.Root!);
        return (result.Root!, analysis);
    }
}
