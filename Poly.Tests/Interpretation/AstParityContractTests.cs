namespace Poly.Tests.Interpretation;

/// <summary>
/// #389: the differential's own contract. `KnownDivergences` empty reads as "the VM and the printed
/// C# agree", which is only true of the corpus — so the corpus's blind spots are a list, and this
/// pins that list exactly. Adding a shape to the corpus means editing this test and removing the
/// entry, which is the point: an entry cannot be dropped silently.
/// <para>
/// The covered-kind claim is a different test: `NodeFactsTotalityTests` reflects over every concrete
/// `Node` subtype and requires a fact plus a corpus case, so a kind cannot be invented without one.
/// </para>
/// </summary>
public class AstParityContractTests {
    /// <summary>
    /// Pinned, not enumerated from the product: the test is the record. A shape that becomes
    /// visible comes off this list and gains a corpus case in the same change.
    /// </summary>
    private static readonly string[] RecordedBlindSpots = [
        "domain-generated async method body (IsAsync + Await + a later statement)",
        "custom awaitable types",
        "resumable state (SuspendNode)",
        "ValueTask and ValueTask<T> method bodies",
    ];

    [Test]
    public async Task KnownDivergences_IsEmpty() {
        await Assert.That(AstParity.KnownDivergences.Count).IsEqualTo(0)
            .Because("a divergence is a writer defect to fix, not an entry to park a red row");
    }

    [Test]
    public async Task BlindSpots_AreExactlyTheRecordedSet() {
        var actual = AstParity.BlindSpots.Select(entry => entry.Shape).OrderBy(s => s, StringComparer.Ordinal).ToList();
        var expected = RecordedBlindSpots.OrderBy(s => s, StringComparer.Ordinal).ToList();
        await Assert.That(string.Join("\n", actual)).IsEqualTo(string.Join("\n", expected))
            .Because("a blind spot cannot be added or removed without updating this record");
    }

    [Test]
    public async Task BlindSpots_SayWhy() {
        var unexplained = AstParity.BlindSpots
            .Where(entry => string.IsNullOrWhiteSpace(entry.Why) || entry.Why.Length < 20)
            .Select(entry => entry.Shape)
            .ToList();
        await Assert.That(unexplained).IsEmpty()
            .Because($"a blind spot without a reason is not a record: {string.Join("; ", unexplained)}");
    }
}