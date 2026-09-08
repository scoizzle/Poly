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
