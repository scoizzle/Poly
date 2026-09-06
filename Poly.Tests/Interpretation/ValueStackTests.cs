using Poly.Interpretation.Vm;

namespace Poly.Tests.Interpretation;

/// <summary>Unit oracles for the pooled VM <see cref="ValueStack"/>.</summary>
public class ValueStackTests {
    [Test]
    public async Task PushPop_RoundTrips() {
        using var stack = new ValueStack(8);
        stack.Push(10L);
        stack.Push(20L);
        await Assert.That(stack.StackPointer).IsEqualTo(2);
        await Assert.That(stack.Pop()).IsEqualTo(20L);
        await Assert.That(stack.Pop()).IsEqualTo(10L);
        await Assert.That(stack.StackPointer).IsEqualTo(0);
    }

    [Test]
    public async Task Pop_Empty_ThrowsUnderflow() {
        using var stack = new ValueStack(4);
        await Assert.That(() => stack.Pop()).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Drop_ReducesHeight() {
        using var stack = new ValueStack(8);
        stack.Push(1L);
        stack.Push(2L);
        stack.Push(3L);
        stack.Drop(2);
        await Assert.That(stack.StackPointer).IsEqualTo(1);
        await Assert.That(stack.Pop()).IsEqualTo(1L);
    }

    [Test]
    public async Task Drop_TooMany_ThrowsUnderflow() {
        using var stack = new ValueStack(4);
        stack.Push(1L);
        await Assert.That(() => stack.Drop(2)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Grow_BeyondInitialCapacity_PreservesValues() {
        using var stack = new ValueStack(2);
        for (long i = 0; i < 64; i++)
            stack.Push(i);
        await Assert.That(stack.StackPointer).IsEqualTo(64);
        await Assert.That(stack.Pop()).IsEqualTo(63L);
        await Assert.That(stack.RawSlots[0]).IsEqualTo(0L);
    }

    [Test]
    public async Task Reset_ClearsHeightWithoutDispose() {
        using var stack = new ValueStack(8);
        stack.Push(99L);
        stack.Reset();
        await Assert.That(stack.StackPointer).IsEqualTo(0);
        stack.Push(7L);
        await Assert.That(stack.Pop()).IsEqualTo(7L);
    }
}
