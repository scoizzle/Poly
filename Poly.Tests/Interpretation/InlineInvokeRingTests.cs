using Poly.Ast.Nodes;
using Poly.Interpretation;

namespace Poly.Tests.Interpretation;

/// <summary>
/// A lambda whose parameters are seeded by <c>SetArgs</c> is compiled inline in a
/// frame parked at the current stack pointer. That body allocates ring slots from
/// its own entry depth upward, so a caller value already in a lower slot stays
/// there. The emitter used to copy the caller's whole ring into
/// <c>_save0.._saveN</c> locals and copy it back around the body; this guards the
/// property those copies were protecting, now that the copies are gone.
/// </summary>
public class InlineInvokeRingTests {
    /// <summary>A lambda with a parameter and an invoke with no argument
    /// expressions: the SetArgs shape, which is the path that took the copies.</summary>
    private static Node SetArgsLambda(long value) =>
        new Lambda([new Parameter("p", TypeReference.To<long>())], new Constant(value));

    /// <summary>Two SetArgs invokes as operands: the first result is live in a ring
    /// slot while the second one runs.</summary>
    private static Node TwoInvokes() =>
        new Add(new Invoke(SetArgsLambda(5L)), new Invoke(SetArgsLambda(7L)));

    [Test, Timeout(10_000)]
    public async Task SetArgsInvoke_DoesNotClobberTheCallersLiveRingSlot(CancellationToken ct) {
        using var exec = Interpreter.Execute(Interpreter.Compile(TwoInvokes()));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(12L);
    }
}