using Poly.Interpretation.Vm;
using Poly.Interpretation.Vm.Abi;

namespace Poly.Tests.Interpretation;

/// <summary>
/// Two rules with the same signature and different questions. These tests pin
/// the split so a future reader does not "fix" one into the other:
/// <list type="bullet">
/// <item><see cref="RingCodec.UnderlyingKindFor"/> asks whether a type's
/// <em>underlying</em> value is long-representable (boxing, array element shapes,
/// unboxing). It peels a nullable, so <c>int?</c> is a word.</item>
/// <item><see cref="ValueRepresentationClassifier.KindFor"/> asks what a
/// <em>node</em> of that type puts in its slot. A nullable is boxed into a heap
/// handle, which is what <c>UsesNullSentinel</c> and <c>UnboxIfHeapNullable</c>
/// already assume.</item>
/// </list>
/// Handle 0 is the ABI null sentinel and is never a live object, so the two
/// deref idioms (guarded and unguarded) agree.
/// </summary>
public class SlotInvariantsTests {
    [Test, Timeout(10_000)]
    public async Task Nullable_UnderlyingValue_IsAWord(CancellationToken ct) {
        await Assert.That(RingCodec.UnderlyingKindFor(typeof(int?)))
            .IsEqualTo(ValueRepresentationKind.StackScalar);
        await Assert.That(RingCodec.IsInline(typeof(int?))).IsTrue();
        await Assert.That(RingCodec.UnderlyingKindFor(typeof(double?)))
            .IsEqualTo(ValueRepresentationKind.StackScalar);
    }

    [Test, Timeout(10_000)]
    public async Task Nullable_SlotKind_IsAHandle(CancellationToken ct) {
        await Assert.That(ValueRepresentationClassifier.KindFor(typeof(int?)))
            .IsEqualTo(ValueRepresentationKind.HeapRef);
        await Assert.That(ValueRepresentationClassifier.KindFor(typeof(double?)))
            .IsEqualTo(ValueRepresentationKind.HeapRef);
        await Assert.That(ValueRepresentationClassifier.KindFor(typeof(bool?)))
            .IsEqualTo(ValueRepresentationKind.HeapRef);
    }

    [Test, Timeout(10_000)]
    public async Task NonNullable_UnderlyingAndSlotKindAgree(CancellationToken ct) {
        foreach (var type in new[] {
            typeof(long), typeof(int), typeof(bool), typeof(char),
            typeof(double), typeof(float), typeof(string), typeof(object),
            typeof(int[]), typeof(DateTime), typeof(decimal),
        }) {
            await Assert.That((ValueRepresentationKind)RingCodec.UnderlyingKindFor(type))
                .IsEqualTo(ValueRepresentationClassifier.KindFor(type));
        }
    }

    /// <summary>A value-type element that is not an inline word is a heap
    /// handle, so its array stores handles; a reference element is not an inline
    /// word either. This is the array-side consequence of the two rules.</summary>
    [Test, Timeout(10_000)]
    public async Task ArrayShape_FollowsTheElementWord(CancellationToken ct) {
        await Assert.That(RingCodec.IsInline(typeof(long))).IsTrue();
        await Assert.That(RingCodec.ArraySlotElementType(typeof(long))).IsEqualTo(typeof(long));
        await Assert.That(RingCodec.ArraySlotElementType(typeof(string))).IsEqualTo(typeof(object));
    }

    // ── Handle 0 is the null sentinel, never a live object ────────

    [Test, Timeout(10_000)]
    public async Task HandleZero_DereferencesToNull(CancellationToken ct) {
        using var heap = new VmManagedObjectHeap();
        await Assert.That(heap.UnsafeGet(0)).IsNull();
        await Assert.That(heap.Get(0)).IsNull();
    }

    [Test, Timeout(10_000)]
    public async Task FirstAllocation_IsNeverHandleZero(CancellationToken ct) {
        using var heap = new VmManagedObjectHeap();
        var handle = heap.Allocate(new object());
        await Assert.That(handle).IsGreaterThan(0);
    }
}