using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Lowering;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// #569: <c>Now</c> is one UTC clock, so a DateTime compared against it is compared as its UTC instant. A value with
/// Kind <c>Local</c> is its <c>ToUniversalTime()</c>; a <c>Utc</c> or <c>Unspecified</c> value keeps its clock time, the
/// same rule the EF column converter writes (#937). The normalization is in the lowered body, which the interpreter
/// and the printed C# both run, so the two sides cannot drift.
/// </summary>
public class DateTimeKindComparisonParityTests {
    const string Dsl = """
        domain Clinic
        uses temporal
        Appointment: entity {
          StartsAt: DateTime
          InFuture: policy { StartsAt > Now }
        }
        """;

    /// <summary>A time 30 minutes ahead on the machine's own clock is in the future, whatever zone the machine is in.</summary>
    [Test]
    public async Task LocalFuture_IsInFuture_OnBothSides() {
        var outcomes = await ParityScenario.FromDsl(Dsl, "DateTimeKind569LocalFuture").AssertAgree(side => {
            side.Create("Appointment", ("StartsAt", DateTime.Now.AddMinutes(30)));
            side.EvaluatePolicy("InFuture");
        });

        await Assert.That(outcomes[^1].State["InFuture"]).IsEqualTo("True")
            .Because("30 minutes ahead on the local clock is the same instant 30 minutes ahead in UTC");
    }

    /// <summary>A local time 30 minutes behind the local clock is in the past: the opposite error, for a zone ahead of UTC.</summary>
    [Test]
    public async Task LocalPast_IsNotInFuture_OnBothSides() {
        var outcomes = await ParityScenario.FromDsl(Dsl, "DateTimeKind569LocalPast").AssertAgree(side => {
            side.Create("Appointment", ("StartsAt", DateTime.Now.AddMinutes(-30)));
            side.EvaluatePolicy("InFuture");
        });

        await Assert.That(outcomes[^1].State["InFuture"]).IsEqualTo("False")
            .Because("30 minutes behind the local clock is the same instant 30 minutes behind in UTC");
    }

    /// <summary>
    /// An Unspecified value IS the UTC clock (the rule #937's store converter writes and reads). It is not shifted:
    /// calling <c>ToUniversalTime()</c> on it would treat it as local and move it by the machine's offset.
    /// </summary>
    [Test]
    public async Task Unspecified_IsComparedAsTheUtcClock_OnBothSides() {
        var outcomes = await ParityScenario.FromDsl(Dsl, "DateTimeKind569Unspecified").AssertAgree(side => {
            side.Create("Appointment", ("StartsAt", DateTime.SpecifyKind(DateTime.UtcNow.AddMinutes(30), DateTimeKind.Unspecified)));
            side.EvaluatePolicy("InFuture");
            side.Create("Appointment", ("StartsAt", DateTime.SpecifyKind(DateTime.UtcNow.AddMinutes(-90), DateTimeKind.Unspecified)));
            side.EvaluatePolicy("InFuture");
        });

        await Assert.That(outcomes[1].State["InFuture"]).IsEqualTo("True")
            .Because("an Unspecified value 30 minutes ahead of the UTC clock is in the future");
        await Assert.That(outcomes[3].State["InFuture"]).IsEqualTo("False")
            .Because("an Unspecified value 90 minutes behind the UTC clock is not, and a local-time reading would move it");
    }

    /// <summary>The lowered policy reads Kind and converts only a Local value, so a Utc value is left as it is.</summary>
    [Test]
    public async Task LoweredPolicy_ComparesAsAnInstant_ThroughTheKindCheck() {
        var domain = EvolvedDomain.FromDsl(Dsl).Domain;
        _ = RuntimeAnalysisCache.GetOrLower(domain);

        await Assert.That(RuntimeAnalysisCache.TryGetPolicyBody(domain, "Appointment", "InFuture", out var body)).IsTrue();
        var members = Nodes(body!).OfType<Member>().Select(m => m.MemberName).ToList();
        await Assert.That(members).Contains("Kind");
        await Assert.That(members).Contains("ToUniversalTime");
        await Assert.That(members).Contains("UtcNow");
        await Assert.That(Nodes(body!).OfType<Conditional>().Any()).IsTrue()
            .Because("the conversion is the Local arm; the Utc and Unspecified arm is the value itself");
    }

    static IEnumerable<Node> Nodes(Node node) {
        yield return node;
        foreach (var child in node.Children) {
            if (child is null)
                continue;
            foreach (var nested in Nodes(child))
                yield return nested;
        }
    }
}