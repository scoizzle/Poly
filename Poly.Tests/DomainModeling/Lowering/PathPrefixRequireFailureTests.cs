using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.Interpretation.CSharp;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// A path-prefix read under <c>require</c> is absence-safe and answers the guide's optional-value rule (#936):
/// a policy that reads an unlinked hop is false, so the action is refused with <c>Policy</c> (never a throw,
/// never <c>Required</c>), and the same policy under <c>require not</c> is true, so the action runs.
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
              room: Room optional
              RoomFree: policy { room Occupied is false }
              Booked: stage {
                CheckIn: action require RoomFree {
                  transition to InHouse
                }
              }
              InHouse: stage { }
            }
            """);
        var exec = ModuleExec.For(domain);
        var reservation = domain.Types.OfType<Entity>().First(e => e.Name == "Reservation");
        var res = exec.Create((reservation).Name, new Dictionary<string, object?>()).Instance!;
        ModuleOutcome? result = null;
        var threw = false;
        try {
            result = exec.Invoke(res, "CheckIn");
        }
        catch (Exception) {
            threw = true;
        }

        await Assert.That(threw).IsFalse();
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Succeeded).IsFalse();
        await Assert.That(result.Error).Contains("blocked by policy 'RoomFree'");
        await Assert.That(result.Failure!.Kind).IsEqualTo(DomainFailureKind.Policy);
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
              room: Room optional
              RoomFree: policy { room Occupied is false }
              Booked: stage {
                CheckIn: action require RoomFree {
                  transition to InHouse
                }
              }
              InHouse: stage { }
            }
            """);
        var types = DomainProgramProjection.ToSyntax(domain, analysis);
        var unit = new CompilationUnitNode([], null, types, null);
        var cs = new CSharpGenerator().Generate(unit);

        await Assert.That(cs).Contains(
            "DomainResult.Failure(Poly.Runtime.DomainFailure.Policy(\"'CheckIn' blocked by policy 'RoomFree'.\", \"Reservation.CheckIn\", \"RoomFree\"))");
        await Assert.That(cs).DoesNotContain("?? throw new InvalidOperationException");
        await Assert.That(cs).Contains("this.Room!");
    }


    [Test]
    public async Task Confirm_RequireNot_UnlinkedSection_Runs() {
        // #936: `not (absent >= 1)` is true, as `not (OptDue < Today)` is true while the optional date is absent.
        var (domain, _) = Evolve("""
            domain EnrollSlice
            Section: entity {
              SeatsTaken: Number default(0)
            }
            Enrollment: entity {
              section: Section optional
              SectionFull: policy { section SeatsTaken >= 1 }
              Pending: stage {
                Confirm: action require not SectionFull {
                  transition to Confirmed
                }
              }
              Confirmed: stage { }
            }
            """);
        var exec = ModuleExec.For(domain);
        var enrollment = domain.Types.OfType<Entity>().First(e => e.Name == "Enrollment");
        var en = exec.Create((enrollment).Name, new Dictionary<string, object?>()).Instance!;
        ModuleOutcome? result = null;
        var threw = false;
        try {
            result = exec.Invoke(en, "Confirm");
        }
        catch (Exception) {
            threw = true;
        }

        await Assert.That(threw).IsFalse();
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Succeeded).IsTrue()
            .Because(result.Error ?? "");
        await Assert.That(ModuleReads.Stage(en)).IsEqualTo("Confirmed");
    }


    [Test]
    public async Task Escalate_MultiHop_UnlinkedReporter_FailsClosed() {
        var (domain, _) = Evolve("""
            domain IssueSlice
            Engineer: entity { team: Team }
            Team: entity { TeamName: Text }
            Issue: entity {
              reporter: Engineer optional
              FromBlueTeam: policy { reporter team TeamName is "Blue" }
              Open: stage {
                Escalate: action require FromBlueTeam { transition to Closed }
              }
              Closed: stage { }
            }
            """);
        var exec = ModuleExec.For(domain);
        var issueE = domain.Types.OfType<Entity>().First(e => e.Name == "Issue");
        var issue = exec.Create((issueE).Name, new Dictionary<string, object?>()).Instance!;
        var result = exec.Invoke(issue, "Escalate");
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Error).Contains("blocked by policy 'FromBlueTeam'");
        await Assert.That(result.Failure!.Kind).IsEqualTo(DomainFailureKind.Policy);
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
        var exec = ModuleExec.For(domain);
        var deviceE = domain.Types.OfType<Entity>().First(e => e.Name == "Device");
        var device = exec.Create((deviceE).Name, new Dictionary<string, object?> { ["Active"] = false }).Instance!;
        var result = exec.Invoke(device, "Submit");
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Error).Contains("IsActive");
    }

    [Test]
    public async Task EmptyBody_RequireNot_Unlinked_Runs() {
        // #936: an empty body still runs the require gates; `not (absent >= 1)` is true.
        var (domain, _) = Evolve("""
            domain EmptyRequireNot
            Section: entity {
              SeatsTaken: Number default(0)
            }
            Enrollment: entity {
              section: Section optional
              SectionFull: policy { section SeatsTaken >= 1 }
              Confirm: action require not SectionFull { }
            }
            """);
        var exec = ModuleExec.For(domain);
        var enrollE = domain.Types.OfType<Entity>().First(e => e.Name == "Enrollment");
        var en = exec.Create((enrollE).Name, new Dictionary<string, object?>()).Instance!;
        var result = exec.Invoke(en, "Confirm");
        await Assert.That(result.Succeeded).IsTrue().Because(result.Error ?? "");
    }

    private static (Domain Domain, AnalysisResult Analysis) Evolve(string poly) {
        var changes = new PolyDslParser(poly).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(changes);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ",
                result.Analysis.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.Message)));
        var analysis = DomainModelAnalyzer.Analyze(result.Root!);
        return (result.Root!, analysis);
    }
}