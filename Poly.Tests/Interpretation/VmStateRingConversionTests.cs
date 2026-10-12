using Poly.Interpretation;
using Poly.Interpretation.Vm;

namespace Poly.Tests.Interpretation;

/// <summary>Ring-slot conversion (<see cref="VmState.ToRing"/>) for the narrow integer kinds.</summary>
public class VmStateRingConversionTests {
    [Test]
    public async Task ToRing_NarrowIntegers_WidenToLong() {
        using var state = new VmState(Interpreter.Compile(new Constant(0L)));
        await Assert.That(state.ToRing((byte)200)).IsEqualTo(200L);
        await Assert.That(state.ToRing((sbyte)-1)).IsEqualTo(-1L);
        await Assert.That(state.ToRing((short)300)).IsEqualTo(300L);
        await Assert.That(state.ToRing((ushort)40000)).IsEqualTo(40000L);
    }
}