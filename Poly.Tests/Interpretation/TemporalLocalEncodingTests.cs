using Poly.Interpretation;
using Poly.Interpretation.Vm.Abi;

namespace Poly.Tests.Interpretation;

/// <summary>
/// S1b / D5. <c>DateTime</c>, <c>DateOnly</c>, and <c>TimeOnly</c> are one word each.
/// <c>DateTimeOffset</c> is 16 bytes and stays a handle. Today all four are handles,
/// and <c>RingCodecTests</c> pins that; this is the characterization that turns.
/// </summary>
public class TemporalLocalEncodingTests {
    [Test]
    public async Task DateTime_IsAStackScalarWord() {
        var stamp = new DateTime(2026, 8, 26, 12, 0, 0, DateTimeKind.Utc);
        await Assert.That(ValueRepresentationClassifier.KindFor(typeof(DateTime)))
            .IsEqualTo(ValueRepresentationKind.StackScalar);
        await Assert.That(RingCodec.TryInlineConstant(stamp, out long word)).IsTrue();
        await Assert.That(word).IsEqualTo(stamp.ToBinary());
    }

    [Test]
    public async Task DateOnly_IsAStackScalarWord() {
        var day = new DateOnly(2026, 8, 26);
        await Assert.That(ValueRepresentationClassifier.KindFor(typeof(DateOnly)))
            .IsEqualTo(ValueRepresentationKind.StackScalar);
        await Assert.That(RingCodec.TryInlineConstant(day, out long word)).IsTrue();
        await Assert.That(word).IsEqualTo(day.DayNumber);
    }

    [Test]
    public async Task TimeOnly_IsAStackScalarWord() {
        var time = new TimeOnly(12, 30, 1);
        await Assert.That(ValueRepresentationClassifier.KindFor(typeof(TimeOnly)))
            .IsEqualTo(ValueRepresentationKind.StackScalar);
        await Assert.That(RingCodec.TryInlineConstant(time, out long word)).IsTrue();
        await Assert.That(word).IsEqualTo(time.Ticks);
    }

    [Test]
    public async Task DateTimeOffset_StaysAHandle() {
        await Assert.That(ValueRepresentationClassifier.KindFor(typeof(DateTimeOffset)))
            .IsEqualTo(ValueRepresentationKind.HeapRef);
        await Assert.That(RingCodec.TryInlineConstant(DateTimeOffset.UnixEpoch, out _)).IsFalse();
    }

    /// <summary>A stored <c>DateTime</c> comes back as that value, including <see cref="DateTimeKind"/>.</summary>
    [Test]
    public async Task DateTimeVariable_RoundTripsKind() {
        var stamp = new DateTime(2026, 8, 26, 12, 0, 0, DateTimeKind.Utc);
        var d = new Variable("d");
        var program = new Block([
            new Assignment(d, new Constant(stamp)),
            d,
        ], [d]);

        using var exec = Interpreter.Execute(Interpreter.Compile(program));
        var back = exec.GetValue<DateTime>();
        await Assert.That(back).IsEqualTo(stamp);
        await Assert.That(back.Kind).IsEqualTo(DateTimeKind.Utc);
    }
}