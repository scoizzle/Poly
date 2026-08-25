namespace Poly.Tests.Syntax.Analysis;

public class NodeMetadataStoreTests {
    // --- Set overwrite semantics ---

    [Test]
    public async Task Set_CalledTwiceForSameNodeAndType_OverwritesWithoutThrowing() {
        var store = new NodeMetadataStore();
        var node = new Constant(1);

        store.Set(node, new ValueMetadata(10));
        store.Set(node, new ValueMetadata(20));

        await Assert.That(store.Get<ValueMetadata>(node)!.Value).IsEqualTo(20);
    }

    // --- RemoveAll ---

    [Test]
    public async Task RemoveAll_ClearsAllMetadataForNode() {
        var store = new NodeMetadataStore();
        var node = new Constant(1);

        store.Set(node, new ValueMetadata(1));
        store.Set(node, new LabelMetadata("x"));
        store.RemoveAll(node);

        await Assert.That(store.Get<ValueMetadata>(node)).IsNull();
        await Assert.That(store.Get<LabelMetadata>(node)).IsNull();
    }

    [Test]
    public async Task RemoveAll_DoesNotAffectOtherNodes() {
        var store = new NodeMetadataStore();
        var nodeA = new Constant(1);
        var nodeB = new Constant(2);

        store.Set(nodeA, new ValueMetadata(1));
        store.Set(nodeB, new ValueMetadata(2));
        store.RemoveAll(nodeA);

        await Assert.That(store.Get<ValueMetadata>(nodeA)).IsNull();
        await Assert.That(store.Get<ValueMetadata>(nodeB)!.Value).IsEqualTo(2);
    }

    // --- GetOrAdd ---

    [Test]
    public async Task GetOrAdd_WhenEntryMissing_CreatesAndReturnsNewEntry() {
        var store = new NodeMetadataStore();
        var node = new Constant(1);

        var result = store.GetOrAdd(node, static () => new ValueMetadata(42));

        await Assert.That(result.Value).IsEqualTo(42);
    }

    [Test]
    public async Task GetOrAdd_WhenEntryExists_ReturnsExistingWithoutCallingFactory() {
        var store = new NodeMetadataStore();
        var node = new Constant(1);

        store.Set(node, new ValueMetadata(7));
        var factoryCalled = false;
        var result = store.GetOrAdd(node, () => { factoryCalled = true; return new ValueMetadata(99); });

        await Assert.That(result.Value).IsEqualTo(7);
        await Assert.That(factoryCalled).IsFalse();
    }

    // --- Inline-to-overflow promotion ---

    [Test]
    public async Task Set_ExceedingInlineCapacity_StillRetrievesAllEntries() {
        var store = new NodeMetadataStore();
        var node = new Constant(1);

        store.Set(node, new ValueMetadata(1));
        store.Set(node, new LabelMetadata("a"));
        store.Set(node, new FlagMetadata(true));
        store.Set(node, new CountMetadata(10));
        store.Set(node, new ValueMetadata(99)); // overwrite in overflow

        await Assert.That(store.Get<ValueMetadata>(node)!.Value).IsEqualTo(99);
        await Assert.That(store.Get<LabelMetadata>(node)!.Label).IsEqualTo("a");
        await Assert.That(store.Get<FlagMetadata>(node)!.Flag).IsTrue();
        await Assert.That(store.Get<CountMetadata>(node)!.Count).IsEqualTo(10);
    }

    // --- Remove<T> ---

    [Test]
    public async Task Remove_RemovesOnlySpecifiedType() {
        var store = new NodeMetadataStore();
        var node = new Constant(1);

        store.Set(node, new ValueMetadata(5));
        store.Set(node, new LabelMetadata("hello"));
        store.Remove<ValueMetadata>(node);

        await Assert.That(store.Get<ValueMetadata>(node)).IsNull();
        await Assert.That(store.Get<LabelMetadata>(node)!.Label).IsEqualTo("hello");
    }

    // --- Remove then add a new type (stale inline slot must not resurface) ---

    [Test]
    public async Task Remove_ThenSetNewType_PromotesFromRemainingEntriesWithoutStaleSlot() {
        var store = new NodeMetadataStore();
        var node = new Constant(1);

        // Fill enough distinct types to force inline mode (≤ 4 entries).
        store.Set(node, new ValueMetadata(1));
        store.Set(node, new LabelMetadata("a"));

        // Remove the entry in the middle of the inline array, then add a new 3rd
        // type. If the vacated trailing slot was not cleared and later (via the
        // promotion loop) read as a live entry, the removed metadata would leak
        // back into the overflow.
        store.Remove<ValueMetadata>(node);
        store.Set(node, new FlagMetadata(true));

        await Assert.That(store.Get<ValueMetadata>(node)).IsNull();
        await Assert.That(store.Get<LabelMetadata>(node)!.Label).IsEqualTo("a");
        await Assert.That(store.Get<FlagMetadata>(node)!.Flag).IsTrue();
    }

    // --- Copy constructor (snapshot) ---

    [Test]
    public async Task CopyConstructor_IsIndependent_OfOriginal() {
        var original = new NodeMetadataStore();
        var node = new Constant(1);

        original.Set(node, new ValueMetadata(1));
        var copy = new NodeMetadataStore(original);

        copy.Set(node, new ValueMetadata(99));

        await Assert.That(original.Get<ValueMetadata>(node)!.Value).IsEqualTo(1);
        await Assert.That(copy.Get<ValueMetadata>(node)!.Value).IsEqualTo(99);
    }

    // --- Test metadata types ---

    private sealed record ValueMetadata(int Value) : IAnalysisMetadata;
    private sealed record LabelMetadata(string Label) : IAnalysisMetadata;
    private sealed record FlagMetadata(bool Flag) : IAnalysisMetadata;
    private sealed record CountMetadata(int Count) : IAnalysisMetadata;
}