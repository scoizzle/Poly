using System.Collections;
using Poly.Interpretation;

namespace Poly.Tests.Interpretation;

/// <summary>F17: Custom IEnumerable whose enumerator is IDisposable — Dispose after completion/break/throw.</summary>
public class ForEachEnumeratorDisposeTests {
    [Test]
    public async Task ForEach_DisposableEnumerator_DisposesAfterCompletion() {
        var coll = new DisposableEnumerable();
        var item = new Variable("item");
        var sum = new Variable("sum");
        var node = new Block([
            new Assignment(sum, new Constant(0L)),
            new ForEachLoop(item, new Constant(coll),
                new Assignment(sum, new Add(sum, item))),
            sum
        ], [sum, item]);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(6L);
        await Assert.That(coll.EnumeratorDisposed).IsTrue();
    }

    [Test]
    public async Task ForEach_DisposableEnumerator_DisposesAfterBreak() {
        var coll = new DisposableEnumerable();
        var item = new Variable("item");
        var node = new ForEachLoop(item, new Constant(coll), new BreakStatement());
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(coll.EnumeratorDisposed).IsTrue();
    }

    [Test]
    public async Task ForEach_DisposableEnumerator_DisposesAfterThrow() {
        var coll = new DisposableEnumerable();
        var item = new Variable("item");
        var node = new ForEachLoop(
            item,
            new Constant(coll),
            new ThrowStatement(new New(TypeReference.To<InvalidOperationException>(), new Constant("x"))));
        try {
            using var exec = Interpreter.Execute(Interpreter.Compile(node));
        }
        catch (InvalidOperationException) { }
        await Assert.That(coll.EnumeratorDisposed).IsTrue();
    }

    private sealed class DisposableEnumerable : IEnumerable<long> {
        public bool EnumeratorDisposed { get; private set; }
        public IEnumerator<long> GetEnumerator() => new Enum(this);
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private sealed class Enum : IEnumerator<long> {
            private readonly DisposableEnumerable _owner;
            private int _i = -1;
            private static readonly long[] Items = [1, 2, 3];
            public Enum(DisposableEnumerable owner) => _owner = owner;
            public long Current => Items[_i];
            object IEnumerator.Current => Current;
            public bool MoveNext() => ++_i < Items.Length;
            public void Reset() => _i = -1;
            public void Dispose() => _owner.EnumeratorDisposed = true;
        }
    }
}
