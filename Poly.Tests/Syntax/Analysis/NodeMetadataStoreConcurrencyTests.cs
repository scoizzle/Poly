using System.Collections.Concurrent;

namespace Poly.Tests.Syntax.Analysis;

/// <summary>
/// Exercises <see cref="NodeMetadataStore"/> from many threads at once.
/// The store is shared by concurrent analysis passes, so every mutation path
/// (Set / Remove / RemoveAll / GetOrAdd) must be safe to call concurrently
/// with readers and other writers.
/// </summary>
public class NodeMetadataStoreConcurrencyTests {
    [Test]
    public async Task Set_FromManyThreads_EachNodeKeepsItsOwnMetadata() {
        var store = new NodeMetadataStore();
        var nodes = Enumerable.Range(0, WriterCount).Select(_ => new Constant(0)).ToArray();

        await Parallel.ForAsync(0, WriterCount, (i, _) => {
            store.Set(nodes[i], new ValueMetadata(i));
            store.Set(nodes[i], new LabelMetadata(i.ToString()));
            return ValueTask.CompletedTask;
        });

        for (var i = 0; i < WriterCount; i++) {
            await Assert.That(store.Get<ValueMetadata>(nodes[i])!.Value).IsEqualTo(i);
            await Assert.That(store.Get<LabelMetadata>(nodes[i])!.Label).IsEqualTo(i.ToString());
        }
    }

    [Test]
    public async Task Set_FromManyThreads_SameNodeAndType_NoLostOrTornWrites() {
        var store = new NodeMetadataStore();
        var node = new Constant(0);

        // Each writer stores its own index. Writes are serialized by the bucket
        // lock, so the surviving value must be one of the written values (0..)
        // — never a partially-written or default value.
        await Parallel.ForAsync(0, WriterCount, (w, _) => {
            store.Set(node, new ValueMetadata(w));
            return ValueTask.CompletedTask;
        });

        var final = store.Get<ValueMetadata>(node)!.Value;
        // Assuming just one parallel worker per index, the surviving value must be
        // one of the written values — never a default, torn, or out-of-range value.
        var validValues = Enumerable.Range(0, WriterCount).ToHashSet();
        await Assert.That(validValues).Contains(final);
    }

    [Test]
    public async Task MixedWriters_ParallelReadersAndRemovers_NoCorruption() {
        var store = new NodeMetadataStore();
        var nodes = Enumerable.Range(0, WriterCount).Select(_ => new Constant(0)).ToArray();
        var spread = new Barrier(4);

        var seeded = Task.Run(() => {
            // Promote every node's bucket to overflow before the racers start.
            for (var i = 0; i < nodes.Length; i++) {
                store.Set(nodes[i], new ValueMetadata(i));
                store.Set(nodes[i], new LabelMetadata($"label-{i}"));
                store.Set(nodes[i], new FlagMetadata(i % 2 == 0));
                store.Set(nodes[i], new CountMetadata(i));
                store.Set(nodes[i], new ExtraMetadata(i));
            }
        });
        await seeded;

        var writer = Task.Run(() => {
            spread.SignalAndWait();
            for (var round = 0; round < 200; round++) {
                for (var i = 0; i < nodes.Length; i++) {
                    store.Set(nodes[i], new CountMetadata(i + round));
                }
            }
        });
        var reader = Task.Run(() => {
            spread.SignalAndWait();
            for (var round = 0; round < 200; round++) {
                for (var i = 0; i < nodes.Length; i++) {
                    _ = store.Get<ValueMetadata>(nodes[i]);
                    _ = store.Get<LabelMetadata>(nodes[i]);
                    _ = store.Get<FlagMetadata>(nodes[i]);
                    _ = store.Get<ExtraMetadata>(nodes[i]);
                    _ = store.GetAll(nodes[i]);
                }
            }
        });
        var remover = Task.Run(() => {
            spread.SignalAndWait();
            for (var round = 0; round < 20; round++) {
                for (var i = 0; i < nodes.Length; i++) {
                    store.Remove<CountMetadata>(nodes[i]);
                    store.Set(nodes[i], new CountMetadata(i));
                }
            }
        });
        var copier = Task.Run(() => {
            spread.SignalAndWait();
            for (var round = 0; round < 20; round++) {
                _ = new NodeMetadataStore(store);
            }
        });

        await Task.WhenAll(writer, reader, remover, copier);

        for (var i = 0; i < nodes.Length; i++) {
            await Assert.That(store.Get<ValueMetadata>(nodes[i])!.Value).IsEqualTo(i);
        }
    }

    [Test]
    public async Task GetOrAdd_FromManyThreads_SameNodeAndType_ReturnsExisting() {
        var store = new NodeMetadataStore();
        var node = new Constant(0);
        var seen = new ConcurrentDictionary<int, byte>();
        var stored = new ValueMetadata(42);

        await Parallel.ForAsync(0, WriterCount, (i, _) => {
            var result = store.GetOrAdd(node, () => stored);
            seen.TryAdd(result.Value, 0);
            return ValueTask.CompletedTask;
        });

        await Assert.That(store.Get<ValueMetadata>(node)).IsSameReferenceAs(stored);
        // The factory must have produced a single shared instance across all callers.
        await Assert.That(seen.Keys).HasSingleItem();
        await Assert.That(seen.Keys.Single()).IsEqualTo(42);
    }

    private const int WriterCount = 16;

    private sealed record ValueMetadata(int Value) : IAnalysisMetadata;
    private sealed record LabelMetadata(string Label) : IAnalysisMetadata;
    private sealed record FlagMetadata(bool Flag) : IAnalysisMetadata;
    private sealed record CountMetadata(int Count) : IAnalysisMetadata;
    private sealed record ExtraMetadata(int Value) : IAnalysisMetadata;
}