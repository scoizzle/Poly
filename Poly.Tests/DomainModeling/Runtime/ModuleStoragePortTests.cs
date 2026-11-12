using System.Collections.Concurrent;

namespace Poly.Tests.DomainModeling.Runtime;

/// <summary>
/// The journaled <see cref="ModuleStoragePort"/> adapter's own contract: the journal and the
/// inner key set are one critical section, and a rollback validates its mark before it
/// reverts anything.
/// </summary>
public class ModuleStoragePortTests {
    [Test]
    public async Task RollbackTo_NegativeMark_ThrowsWithoutRevertingKeys() {
        var port = new ModuleStoragePort();
        await Assert.That(port.TryReserve("Patron", "Email", "a@b.c")).IsTrue();
        var mark = port.Mark();
        await Assert.That(port.TryReserve("Patron", "Email", "b@b.c")).IsTrue();

        // A negative mark must be refused before the revert loop walks off the journal: on
        // master the loop releases every key and then throws from the indexer.
        await Assert.That(() => port.RollbackTo(-1)).Throws<ArgumentOutOfRangeException>();

        await Assert.That(port.Exists("Patron", "Email", "b@b.c")).IsTrue();
        await Assert.That(port.Exists("Patron", "Email", "a@b.c")).IsTrue();
        await Assert.That(port.Mark()).IsEqualTo(mark + 1);
    }

    [Test]
    public async Task RollbackTo_MarkPastEnd_ThrowsWithoutRevertingKeys() {
        var port = new ModuleStoragePort();
        await Assert.That(port.TryReserve("Patron", "Email", "a@b.c")).IsTrue();
        var mark = port.Mark();

        await Assert.That(() => port.RollbackTo(mark + 1)).Throws<ArgumentOutOfRangeException>();

        await Assert.That(port.Exists("Patron", "Email", "a@b.c")).IsTrue();
        await Assert.That(port.Mark()).IsEqualTo(mark);
    }

    [Test]
    public async Task RollbackTo_BlockedRelease_RacerReservationSurvives() {
        // The journal and the inner key set are one critical section. A reservation made while
        // a rollback is inside the inner port must not be truncated by the rollback's
        // RemoveRange: on master the racer's entry is dropped and its key stays reserved.
        var inner = new BlockingStoragePort();
        var port = new ModuleStoragePort(inner);

        await Assert.That(port.TryReserve("Patron", "Email", "x@b.c")).IsTrue();
        inner.ArmOnRelease("Patron", "Email", "x@b.c");

        var rollback = Task.Run(() => port.RollbackTo(0));
        await Assert.That(inner.WaitEntered(TimeSpan.FromSeconds(30))).IsTrue()
            .Because("the rollback must reach inner.Release to park inside the critical section");

        var racer = Task.Run(() => port.TryReserve("Patron", "Email", "y@b.c"));

        // On master the racer is unsynchronised and finishes here; under the lock it waits for
        // the rollback. The wait only bounds the green path, so it cannot produce a false red.
        _ = racer.Wait(TimeSpan.FromSeconds(1));

        inner.Release();
        await rollback.WaitAsync(TimeSpan.FromSeconds(30));
        await racer.WaitAsync(TimeSpan.FromSeconds(30));

        port.RollbackTo(0);
        await Assert.That(port.Exists("Patron", "Email", "y@b.c")).IsFalse();
        await Assert.That(port.Mark()).IsEqualTo(0);
    }

    [Test]
    public async Task TryReserve_200ThreadsDistinctKeys_JournalsEveryReservation() {
        // Stress guard: distinct keys per (thread, index), so every call wins and journals one
        // entry. An unsynchronised List<T>.Add loses entries, so the mark under-reports and the
        // rollback leaves keys reserved.
        const int threadCount = 200;
        const int keyCount = 50;
        var port = new ModuleStoragePort();
        var failures = new ConcurrentQueue<Exception>();
        using var barrier = new Barrier(threadCount);

        var threads = new Thread[threadCount];
        for (var t = 0; t < threadCount; t++) {
            var threadIndex = t;
            // Background, so a thread that failed to start cannot hold the test host open; the
            // checked barrier timeout surfaces the shortfall as a failure, not a hang.
            threads[threadIndex] = new Thread(() => {
                try {
                    if (!barrier.SignalAndWait(TimeSpan.FromSeconds(60)))
                        throw new TimeoutException($"Thread {threadIndex} did not reach the barrier.");
                    for (var k = 0; k < keyCount; k++) {
                        var value = $"t{threadIndex}k{k}@example.test";
                        if (!port.TryReserve("Patron", "Email", value))
                            throw new InvalidOperationException($"distinct key {value} was already reserved");
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

        // The journal is append-only, so a lost entry is a lost mark, not a lost key.
        await Assert.That(port.Mark()).IsEqualTo(threadCount * keyCount);

        port.RollbackTo(0);

        await Assert.That(port.Mark()).IsEqualTo(0);
        var survivors = new List<string>();
        for (var t = 0; t < threadCount; t++)
            for (var k = 0; k < keyCount; k++) {
                var value = $"t{t}k{k}@example.test";
                if (port.Exists("Patron", "Email", value))
                    survivors.Add(value);
            }
        await Assert.That(survivors.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Release_200ThreadsOneHeldKey_JournalsOneRemove() {
        // Exists-then-Release-then-journal is one critical section, so the revert log carries
        // exactly one entry however many threads observe the key: a lost journal entry here
        // leaves the key released with no way to restore it.
        const int threadCount = 200;
        var port = new ModuleStoragePort();
        await Assert.That(port.TryReserve("Patron", "Email", "a@b.c")).IsTrue();
        var mark = port.Mark();
        var failures = new ConcurrentQueue<Exception>();
        using var barrier = new Barrier(threadCount);

        RunTogether(threadCount, barrier, failures, _ => port.Release("Patron", "Email", "a@b.c"));

        await Assert.That(failures.Count).IsEqualTo(0);
        await Assert.That(port.Mark() - mark).IsEqualTo(1);
        await Assert.That(port.Exists("Patron", "Email", "a@b.c")).IsFalse();
    }

    [Test]
    public async Task Record_200ThreadsOneKey_JournalsOneAdd() {
        // A reserve that loses journals nothing, so 200 racing records leave one Add entry.
        const int threadCount = 200;
        var port = new ModuleStoragePort();
        var mark = port.Mark();
        var failures = new ConcurrentQueue<Exception>();
        using var barrier = new Barrier(threadCount);

        RunTogether(threadCount, barrier, failures, _ => port.Record("Patron", "Email", "a@b.c"));

        await Assert.That(failures.Count).IsEqualTo(0);
        await Assert.That(port.Mark() - mark).IsEqualTo(1);
        await Assert.That(port.Exists("Patron", "Email", "a@b.c")).IsTrue();

        port.RollbackTo(mark);
        await Assert.That(port.Mark()).IsEqualTo(mark);
        await Assert.That(port.Exists("Patron", "Email", "a@b.c")).IsFalse();
    }

    private static void RunTogether(
        int threadCount, Barrier barrier, ConcurrentQueue<Exception> failures, Action<int> body) {
        var threads = new Thread[threadCount];
        for (var t = 0; t < threadCount; t++) {
            var threadIndex = t;
            threads[threadIndex] = new Thread(() => {
                try {
                    if (!barrier.SignalAndWait(TimeSpan.FromSeconds(60)))
                        throw new TimeoutException($"Thread {threadIndex} did not reach the barrier.");
                    body(threadIndex);
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
    }

    // Delegates to the thread-safe reference port, and parks the first Release of one armed
    // key so a test can hold the adapter inside its critical section.
    private sealed class BlockingStoragePort : IStoragePort {
        private readonly InMemoryStoragePort _inner = new();
        private readonly ManualResetEventSlim _entered = new(false);
        private readonly ManualResetEventSlim _gate = new(false);
        private string? _armedKey;

        public void ArmOnRelease(string entity, string property, string value) {
            _armedKey = KeyCodec.Canonical(entity, property, value);
            _entered.Reset();
            _gate.Reset();
        }

        public bool WaitEntered(TimeSpan timeout) => _entered.Wait(timeout);

        public void Release() => _gate.Set();

        public bool Exists(string entity, string property, string value) =>
            _inner.Exists(entity, property, value);

        public bool TryReserve(string entity, string property, string value) =>
            _inner.TryReserve(entity, property, value);

        public void Release(string entity, string property, string value) {
            if (string.Equals(KeyCodec.Canonical(entity, property, value), _armedKey, StringComparison.Ordinal)) {
                _armedKey = null;
                _entered.Set();
                if (!_gate.Wait(TimeSpan.FromSeconds(60)))
                    throw new TimeoutException("The blocking storage double was never released.");
            }
            _inner.Release(entity, property, value);
        }

        public void Record(string entity, string property, string value) =>
            _inner.Record(entity, property, value);
    }
}