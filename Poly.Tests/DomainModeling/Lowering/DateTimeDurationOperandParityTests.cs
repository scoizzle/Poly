using Poly.DomainModeling;
using Poly.DomainModeling.Evolution;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// #654: <c>DateTime + &lt;Duration-typed value&gt;</c> (a property or an action parameter,
/// not a literal like <c>24 Hours</c>) was accepted at define/check but could never run:
/// the host reported 'AddDays' with no matching member. A Duration is a <c>TimeSpan</c> on
/// the C# surface, so <c>DateTime.Add(TimeSpan)</c> is directly expressible; the simulator
/// and the printed C# must agree, and a Date (no TimeSpan add) fails closed at define.
/// </summary>
public class DateTimeDurationOperandParityTests {
    private const string PropertyDsl = """
        domain Slots
        uses temporal
        Slot: entity {
          Start: DateTime
          Len: Duration
          Stop: DateTime optional
          Extend: action { assign Stop to Start + Len }
        }
        """;

    private const string ParameterDsl = """
        domain Slots
        uses temporal
        Slot: entity {
          Stop: DateTime optional
          Extend: action (s: DateTime, l: Duration) { assign Stop to s + l }
        }
        """;

    private static readonly DateTime Start = new(2031, 1, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Length = TimeSpan.FromMinutes(90);
    private static readonly DateTime Expected = Start + Length;

    /// <summary>Accepted by analysis implies the simulator, the generated C# and <c>polyc check</c> accept too.</summary>
    [Test]
    public async Task DateTimePlusDurationProperty_AnalysisAcceptanceHoldsAtEveryDoor() {
        var gate = GateParity.Check(PropertyDsl,
            SimStep.Create("Slot", """{"Start":"2031-01-01T10:00:00Z","Len":"01:30:00"}""", "slot"),
            SimStep.Invoke("slot", "Extend"));

        await Assert.That(gate.Analysis).IsTrue().Because(gate.ToString());
        await Assert.That(gate.Holds).IsTrue().Because(gate.ToString());
    }

    [Test]
    public async Task Policy_DateTimePlusDurationComparison_AgreesOnBothHosts() {
        var outcomes = await ParityScenario.FromDsl("""
            domain Slots
            uses temporal
            Slot: entity {
              Start: DateTime
              Len: Duration
              Cutoff: DateTime
              Overdue: policy { Start + Len < Cutoff }
            }
            """, "ParityPolicyDateTimePlusDuration")
            .AssertAgree(side => {
                side.Create("Slot", ("Start", Start), ("Len", Length), ("Cutoff", Start.AddHours(2)));
                side.EvaluatePolicy("Overdue");
            });

        await Assert.That(outcomes[1].Success).IsTrue()
            .Because($"step failed: {outcomes[1].ExceptionType} {outcomes[1].Message}");
        await Assert.That(outcomes[1].State["Overdue"]).IsEqualTo("True");
    }

    /// <summary>An optional Duration has nothing to add while it is absent, so it fails closed at define.</summary>
    [Test]
    public async Task OptionalDurationOperand_IsRefusedAtDefine() {
        await Assert.That(() => EvolvedDomain.FromDsl("""
            domain Slots
            uses temporal
            Slot: entity {
              Start: DateTime
              Len: Duration optional
              Stop: DateTime optional
              Extend: action { assign Stop to Start + Len }
            }
            """))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("optional Duration");
    }

    [Test]
    public async Task DateTimeProperty_PlusDurationProperty_AgreesOnBothHosts() {
        var outcomes = await ParityScenario.FromDsl(PropertyDsl, "ParityDateTimePlusDurationProperty")
            .AssertAgree(side => {
                side.Create("Slot", ("Start", Start), ("Len", Length));
                side.Invoke("Extend");
            });

        await Assert.That(outcomes[1].Success).IsTrue()
            .Because($"step failed: {outcomes[1].ExceptionType} {outcomes[1].Message}");
        await Assert.That(outcomes[1].State["Stop"]).IsEqualTo(Expected.ToString());
    }

    [Test]
    public async Task DateTimeParameter_PlusDurationParameter_AgreesOnBothHosts() {
        var outcomes = await ParityScenario.FromDsl(ParameterDsl, "ParityDateTimePlusDurationParameter")
            .AssertAgree(side => {
                side.Create("Slot");
                side.Invoke("Extend", ("s", Start), ("l", Length));
            });

        await Assert.That(outcomes[1].Success).IsTrue()
            .Because($"step failed: {outcomes[1].ExceptionType} {outcomes[1].Message}");
        await Assert.That(outcomes[1].State["Stop"]).IsEqualTo(Expected.ToString());
    }

    [Test]
    public async Task DateTimeProperty_MinusDurationProperty_AgreesOnBothHosts() {
        var outcomes = await ParityScenario.FromDsl("""
            domain Slots
            uses temporal
            Slot: entity {
              Start: DateTime
              Len: Duration
              Began: DateTime optional
              Retreat: action { assign Began to Start - Len }
            }
            """, "ParityDateTimeMinusDurationProperty")
            .AssertAgree(side => {
                side.Create("Slot", ("Start", Start), ("Len", Length));
                side.Invoke("Retreat");
            });

        await Assert.That(outcomes[1].Success).IsTrue()
            .Because($"step failed: {outcomes[1].ExceptionType} {outcomes[1].Message}");
        await Assert.That(outcomes[1].State["Began"]).IsEqualTo((Start - Length).ToString());
    }

    [Test]
    public async Task Now_PlusDurationProperty_AgreesOnBothHosts() {
        var outcomes = await ParityScenario.FromDsl("""
            domain Slots
            uses temporal
            Slot: entity {
              Len: Duration
              Stop: DateTime optional
              Mark: action { assign Stop to Now + Len }
            }
            """, "ParityNowPlusDurationProperty")
            .AssertAgree(side => {
                side.Create("Slot", ("Len", Length));
                side.Invoke("Mark");
            });

        await Assert.That(outcomes[1].Success).IsTrue()
            .Because($"step failed: {outcomes[1].ExceptionType} {outcomes[1].Message}");
        await Assert.That(outcomes[1].State["Stop"]).IsNotNull();
    }

    /// <summary>A nested offset keeps the DateTime family: <c>(Start + Len) + Len</c> adds both parts.</summary>
    [Test]
    public async Task NestedDurationOffset_AgreesOnBothHosts() {
        var outcomes = await ParityScenario.FromDsl("""
            domain Slots
            uses temporal
            Slot: entity {
              Start: DateTime
              Len: Duration
              Stop: DateTime optional
              ExtendTwice: action { assign Stop to (Start + Len) + Len }
            }
            """, "ParityNestedDurationOffset")
            .AssertAgree(side => {
                side.Create("Slot", ("Start", Start), ("Len", Length));
                side.Invoke("ExtendTwice");
            });

        await Assert.That(outcomes[1].Success).IsTrue()
            .Because($"step failed: {outcomes[1].ExceptionType} {outcomes[1].Message}");
        await Assert.That(outcomes[1].State["Stop"]).IsEqualTo((Start + Length + Length).ToString());
    }

    /// <summary>A path read has no Temporal lowering the handlers can reach, so it fails closed at define (#654).</summary>
    [Test]
    public async Task PathToDateTime_PlusDurationProperty_IsRefusedAtDefine() {
        await Assert.That(() => EvolvedDomain.FromDsl("""
            domain Slots
            uses temporal
            Part: entity {
              Start: DateTime
            }
            Slot: entity {
              Len: Duration
              part: Part
              Stop: DateTime optional
              Extend: action { assign Stop to part Start + Len }
            }
            """))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("a date offset by a Duration value is supported only on a date property");
    }

    /// <summary>A Duration read through a link has no lowering the handlers can name, so it fails closed at define.</summary>
    [Test]
    public async Task DurationThroughALink_IsRefusedAtDefine() {
        await Assert.That(() => EvolvedDomain.FromDsl("""
            domain Slots
            uses temporal
            Part: entity {
              Len: Duration
            }
            Slot: entity {
              Start: DateTime
              part: Part optional
              Stop: DateTime optional
              Extend: action { assign Stop to Start + part Len }
            }
            """))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("a Duration offset operand must be a Duration property");
    }

    /// <summary>A Date has no TimeSpan add, so the same shape fails closed at define, naming the operand.</summary>
    [Test]
    public async Task DateProperty_PlusDurationProperty_IsRefusedAtDefine() {
        await Assert.That(() => EvolvedDomain.FromDsl("""
            domain Slots
            uses temporal
            Slot: entity {
              Day: Date
              Len: Duration
              Stop: Date optional
              Bad: action { assign Stop to Day + Len }
            }
            """))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("a Date plus or minus a Duration value is not supported");
    }
}