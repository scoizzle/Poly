using Poly.DomainModeling;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.Interpretation;

/// <summary>
/// F16: a skipped <c>if</c> must not pass the compared Number to
/// <c>DomainResult.Success</c>'s optional argument as a heap handle.
/// F15: a fractional Number is rejected, not stored as 0.
/// </summary>
public class SkippedIfCreateInVmTests {
    private const string Billing = """
        domain Billing

        Reading: entity {
          Usage: Number
          notes: many Note

          FlagHigh: action {
            if (Usage > 900) {
              create in notes { Body: "high usage" }
            }
          }
        }

        Note: entity {
          Body: Text
        }
        """;

    [Test]
    public async Task FlagHigh_UsageAround256_SkipDoesNotCrashAndTakeCreatesNote() {
        var (domain, reading) = Evolve();
        var low = Instance(domain, reading, 255);
        var edge = Instance(domain, reading, 256);
        var high = Instance(domain, reading, 901);

        var skipped = low.InvokeAction("FlagHigh");
        await Assert.That(skipped.Succeeded).IsTrue();
        await Assert.That(low.CreatedChildren.Count).IsEqualTo(0);

        var edgeResult = edge.InvokeAction("FlagHigh");
        await Assert.That(edgeResult.Succeeded).IsTrue();
        await Assert.That(edge.CreatedChildren.Count).IsEqualTo(0);

        var taken = high.InvokeAction("FlagHigh");
        await Assert.That(taken.Succeeded).IsTrue();
        await Assert.That(high.CreatedChildren.Count).IsEqualTo(1);
        await Assert.That(high.CreatedChildren[0].GetProperty<string>("Body")).IsEqualTo("high usage");
    }

    [Test]
    public async Task CreateInstance_FractionalNumber_IsRejected() {
        var (sessionId, _) = McpTestTools.Store.Create("Billing");
        var applied = McpTestTools.Dsl.ApplyDsl(sessionId, """
            domain Billing

            Meter: entity {
              Rate: Number
            }
            """);
        await Assert.That(applied.Success).IsTrue();

        var fraction = McpTestTools.Runtime.CreateInstance(sessionId, "Meter", """{"Rate":0.25}""");
        await Assert.That(fraction.Success).IsFalse();
        await Assert.That(fraction.Message).Contains("Number is integral");
        await Assert.That(fraction.Message).Contains("Decimal");
        McpTestTools.Store.TryGet(sessionId, out var afterFraction);
        await Assert.That(afterFraction!.InstanceMap.Count).IsEqualTo(0);

        var whole = McpTestTools.Runtime.CreateInstance(sessionId, "Meter", """{"Rate":1}""");
        await Assert.That(whole.Success).IsTrue();

        var integralJson = McpTestTools.Runtime.CreateInstance(sessionId, "Meter", """{"Rate":1.0}""");
        await Assert.That(integralJson.Success).IsTrue();

        var edge = McpTestTools.Runtime.CreateInstance(sessionId, "Meter", """{"Rate":256}""");
        await Assert.That(edge.Success).IsTrue();
        await Assert.That(Rates(sessionId)).IsEquivalentTo(new long[] { 1L, 1L, 256L });

        var outOfRange = McpTestTools.Runtime.CreateInstance(sessionId, "Meter", """{"Rate":1e30}""");
        await Assert.That(outOfRange.Success).IsFalse();

        await Assert.That(DictionaryBackedValue.CoerceReadValue(1.0d, Poly.Introspection.PrimitiveType.Int64)).IsEqualTo(1L);
        await Assert.That(DictionaryBackedValue.CoerceReadValue(1.0m, Poly.Introspection.PrimitiveType.Int64)).IsEqualTo(1L);
        var fractional = Assert.Throws<InvalidOperationException>(
            () => DictionaryBackedValue.CoerceReadValue(0.25d, Poly.Introspection.PrimitiveType.Int64));
        await Assert.That(fractional!.Message).Contains("Number is integral");
        await Assert.That(fractional.Message).Contains("Decimal");
    }

    private static long[] Rates(string sessionId) {
        McpTestTools.Store.TryGet(sessionId, out var state);
        return state!.InstanceMap.Values.Select(meter => meter.GetProperty<long>("Rate")).ToArray();
    }

    private static (Domain Domain, Entity Reading) Evolve() {
        var changes = new PolyDslParser(Billing).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(changes);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ", result.Analysis.Diagnostics
                .Where(d => d.Severity == Poly.Analysis.DiagnosticSeverity.Error)
                .Select(d => d.Message)));
        var reading = result.Root!.Types.OfType<Entity>().First(t => t.Name == "Reading");
        return (result.Root, reading);
    }

    private static DomainEntityInstance Instance(Domain domain, Entity reading, long usage) =>
        DomainEntityInstance.Create(reading,
            new Dictionary<string, object?> { ["Usage"] = usage },
            domain);
}