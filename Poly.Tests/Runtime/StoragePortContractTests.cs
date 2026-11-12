using System.Globalization;

using Poly.Runtime;

namespace Poly.Tests.Runtime;

/// <summary>
/// The <see cref="IStoragePort"/> contract: the canonical key encoding, the atomic
/// reserve, the reference port's thread safety and the journaled adapter's rollback.
/// </summary>
public class StoragePortContractTests {
    [Test]
    public async Task KeyCodec_Canonical_IsInjectiveForSeparatorsInMembers() {
        // A member may contain the old unit separator; the encoding may not collapse the pair.
        await Assert.That(KeyCodec.Canonical("a\u001fb", "c"))
            .IsNotEqualTo(KeyCodec.Canonical("a", "b\u001fc"));

        // A null part is its own shape, never the text "null".
        await Assert.That(KeyCodec.Canonical((object?)null)).IsNotEqualTo(KeyCodec.Canonical("null"));

        // No parts and one empty part are different keys.
        await Assert.That(KeyCodec.Canonical("")).IsNotEqualTo(KeyCodec.Canonical());

        // Part order is part of the key.
        await Assert.That(KeyCodec.Canonical("a", "b")).IsNotEqualTo(KeyCodec.Canonical("b", "a"));
    }

    [Test]
    public async Task KeyCodec_Canonical_FormatsUnderInvariantCulture() {
        using var _ = new GermanCultureScope();

        await Assert.That(KeyCodec.Canonical(1.5m)).IsEqualTo("3:1.5");
        await Assert.That(KeyCodec.Canonical(1.5m)).IsNotEqualTo(KeyCodec.Canonical("1,5"));
    }

    [Test]
    public async Task KeyCodec_Canonical_EncodesNullPart() {
        await Assert.That(KeyCodec.Canonical((object?)null)).IsEqualTo("~");
    }

    [Test]
    public async Task KeyCodec_Canonical_NullArray_Throws() {
        await Assert.That(() => KeyCodec.Canonical((object?[])null!))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task KeyCodec_Canonical_RejectsUnsupportedTypes() {
        await Assert.That(() => KeyCodec.Canonical(new object())).Throws<ArgumentException>();
    }

    [Test]
    public async Task KeyCodec_Canonical_StringOverload_AgreesWithArrayForm() {
        await Assert.That(KeyCodec.Canonical("Patron", "Email", "a@b.c"))
            .IsEqualTo(KeyCodec.Canonical(new object?[] { "Patron", "Email", "a@b.c" }));
    }

    [Test]
    public async Task IStoragePort_DeclaresAtomicTryReserve() {
        await Assert.That(typeof(IStoragePort).Namespace).IsEqualTo("Poly.Runtime");

        var tryReserve = typeof(IStoragePort).GetMethod("TryReserve");
        await Assert.That(tryReserve).IsNotNull();
        await Assert.That(tryReserve!.ReturnType).IsEqualTo(typeof(bool));
        var parameters = tryReserve.GetParameters();
        await Assert.That(parameters.Length).IsEqualTo(3);
        await Assert.That(parameters.All(p => p.ParameterType == typeof(string))).IsTrue();
    }

    [Test]
    public async Task TryReserve_200ParallelSameKey_ExactlyOneWins() {
        // 200 dedicated threads, released together, race 500 keys through one port. A
        // check-then-add without the lock lets two threads win the same key.
        const int threadCount = 200;
        const int keyCount = 500;
        var port = new InMemoryStoragePort();
        var wins = new int[keyCount];
        var failures = new System.Collections.Concurrent.ConcurrentQueue<Exception>();
        using var barrier = new Barrier(threadCount);

        var threads = new Thread[threadCount];
        for (var t = 0; t < threadCount; t++) {
            // Background, so a thread that failed to start cannot hold the test host open;
            // the barrier timeout then surfaces the shortfall as a failure, not a hang.
            threads[t] = new Thread(() => {
                try {
                    barrier.SignalAndWait(TimeSpan.FromSeconds(30));
                    for (var k = 0; k < keyCount; k++) {
                        if (port.TryReserve("Patron", "Email", $"patron{k}@example.test"))
                            Interlocked.Increment(ref wins[k]);
                    }
                }
                catch (Exception exception) {
                    failures.Enqueue(exception);
                }
            }) {
                IsBackground = true,
            };
        }

        foreach (var thread in threads)
            thread.Start();
        foreach (var thread in threads)
            thread.Join();

        await Assert.That(failures.Count).IsEqualTo(0);
        for (var k = 0; k < keyCount; k++) {
            var key = k;
            await Assert.That(port.Exists("Patron", "Email", $"patron{key}@example.test")).IsTrue();
            await Assert.That(wins[k]).IsEqualTo(1);
        }

        for (var k = 0; k < keyCount; k++)
            port.Release("Patron", "Email", $"patron{k}@example.test");
        await Assert.That(port.Exists("Patron", "Email", "patron0@example.test")).IsFalse();
        await Assert.That(port.TryReserve("Patron", "Email", "patron0@example.test")).IsTrue();
    }

    [Test]
    public async Task InMemoryStoragePort_NullArguments_ThrowBeforeMutation() {
        var port = new InMemoryStoragePort();

        await Assert.That(() => port.TryReserve(null!, "Email", "a@b.c")).Throws<ArgumentNullException>();
        await Assert.That(() => port.TryReserve("Patron", null!, "a@b.c")).Throws<ArgumentNullException>();
        await Assert.That(() => port.TryReserve("Patron", "Email", null!)).Throws<ArgumentNullException>();
        await Assert.That(() => port.Exists(null!, "Email", "a@b.c")).Throws<ArgumentNullException>();
        await Assert.That(() => port.Exists("Patron", null!, "a@b.c")).Throws<ArgumentNullException>();
        await Assert.That(() => port.Exists("Patron", "Email", null!)).Throws<ArgumentNullException>();
        await Assert.That(() => port.Record(null!, "Email", "a@b.c")).Throws<ArgumentNullException>();
        await Assert.That(() => port.Release("Patron", "Email", null!)).Throws<ArgumentNullException>();

        // Nothing was reserved by any of the failed calls.
        await Assert.That(port.Exists("Patron", "Email", "a@b.c")).IsFalse();
        await Assert.That(port.TryReserve("Patron", "Email", "a@b.c")).IsTrue();
    }

    [Test]
    public async Task ModuleStoragePort_TryReserve_RollsBackWithMark() {
        var port = new ModuleStoragePort();
        port.Record("Patron", "Email", "before@b.c");
        var mark = port.Mark();

        await Assert.That(port.TryReserve("Patron", "Email", "a@b.c")).IsTrue();
        await Assert.That(port.TryReserve("Patron", "Email", "a@b.c")).IsFalse();
        await Assert.That(port.TryReserve("Patron", "Email", "b@b.c")).IsTrue();
        await Assert.That(port.Mark() - mark).IsEqualTo(2);

        // A pre-mark key released after the mark is restored by the rollback.
        port.Release("Patron", "Email", "before@b.c");

        port.RollbackTo(mark);

        await Assert.That(port.Exists("Patron", "Email", "a@b.c")).IsFalse();
        await Assert.That(port.Exists("Patron", "Email", "b@b.c")).IsFalse();
        await Assert.That(port.Exists("Patron", "Email", "before@b.c")).IsTrue();
        await Assert.That(port.Mark()).IsEqualTo(mark);
    }

    [Test]
    public async Task ModuleStoragePort_ReleaseUnknownKey_JournalsNothing() {
        var port = new ModuleStoragePort();
        var mark = port.Mark();

        port.Release("Patron", "Email", "absent@b.c");
        await Assert.That(port.Mark()).IsEqualTo(mark);

        // A phantom journal entry would re-add the key on rollback.
        port.RollbackTo(mark);
        await Assert.That(port.Exists("Patron", "Email", "absent@b.c")).IsFalse();
    }

    [Test]
    public async Task ModuleStoragePort_NullArguments_ThrowBeforeMutation() {
        var port = new ModuleStoragePort();
        var mark = port.Mark();

        await Assert.That(() => port.TryReserve(null!, "Email", "a@b.c")).Throws<ArgumentNullException>();
        await Assert.That(() => port.Record("Patron", null!, "a@b.c")).Throws<ArgumentNullException>();
        await Assert.That(() => port.Release("Patron", "Email", null!)).Throws<ArgumentNullException>();
        await Assert.That(() => port.Exists("Patron", "Email", null!)).Throws<ArgumentNullException>();

        await Assert.That(port.Mark()).IsEqualTo(mark);
        await Assert.That(port.Exists("Patron", "Email", "a@b.c")).IsFalse();
    }

    // Sets de-DE for the current test's async flow and restores the previous cultures.
    private sealed class GermanCultureScope : IDisposable {
        private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
        private readonly CultureInfo _uiCulture = CultureInfo.CurrentUICulture;

        public GermanCultureScope() {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");
        }

        public void Dispose() {
            CultureInfo.CurrentCulture = _culture;
            CultureInfo.CurrentUICulture = _uiCulture;
        }
    }
}