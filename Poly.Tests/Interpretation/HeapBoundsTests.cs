using Poly.Interpretation.Vm;

namespace Poly.Tests.Interpretation;

/// <summary>
/// Bounds and error paths of the VM object heap. Handle 0 stays the ABI null
/// sentinel and any out-of-range handle must fail loud rather than read garbage.
/// </summary>
public class HeapBoundsTests {
    [Test]
    public async Task Get_ZeroHandle_ReturnsNullSentinel() {
        var heap = new VmManagedObjectHeap();
        await Assert.That(heap.Get(0)).IsNull();
    }

    [Test]
    public async Task Get_OutOfRangeHandle_Throws() {
        var heap = new VmManagedObjectHeap();
        await Assert.That(() => heap.Get(5)).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task Set_OutOfRangeHandle_Throws() {
        var heap = new VmManagedObjectHeap();
        await Assert.That(() => heap.Set(5, "x")).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task Recycle_OutOfRangeHandle_Throws() {
        var heap = new VmManagedObjectHeap();
        await Assert.That(() => heap.Recycle(5)).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task Clear_ReusesTheBackingArray() {
        var heap = new VmManagedObjectHeap();
        var raw = heap.RawSlots;
        heap.Allocate("x");
        heap.Clear();
        await Assert.That(ReferenceEquals(raw, heap.RawSlots)).IsTrue();
    }

    [Test]
    public async Task Dispose_IsIdempotent() {
        var heap = new VmManagedObjectHeap();
        heap.Dispose();
        heap.Dispose();
        await Assert.That(heap.Count).IsEqualTo(1);
    }

    [Test]
    public async Task UnsafeSet_ThenUnsafeGet_RoundTrips() {
        var heap = new VmManagedObjectHeap();
        var handle = heap.Allocate("a");
        heap.UnsafeSet(handle, "b");
        await Assert.That(heap.UnsafeGet(handle)).IsEqualTo("b");
    }
}