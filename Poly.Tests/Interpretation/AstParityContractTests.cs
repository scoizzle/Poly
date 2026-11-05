namespace Poly.Tests.Interpretation;

/// <summary>
/// #389: the differential's own contract. `KnownDivergences` empty reads as "the VM and the printed
/// C# agree", which is only true of the corpus — so the corpus's blind spots are a list, and this
/// pins that the list is not quietly emptied and that every entry says why.
/// </summary>
public class AstParityContractTests {
    [Test]
    public async Task KnownDivergences_IsEmpty() {
        await Assert.That(AstParity.KnownDivergences.Count).IsEqualTo(0)
            .Because("a divergence is a writer defect to fix, not an entry to park a red row");
    }

    [Test]
    public async Task BlindSpots_AreDistinct_AndSayWhy() {
        var blindSpots = AstParity.BlindSpots;
        await Assert.That(blindSpots.Count).IsGreaterThan(0)
            .Because("an empty blind-spot list would claim the corpus sees everything");

        var shapes = blindSpots.Select(entry => entry.Shape).ToList();
        await Assert.That(shapes.Distinct(StringComparer.Ordinal).Count()).IsEqualTo(shapes.Count)
            .Because("the same shape twice hides that one of them has no reason");

        var unexplained = blindSpots
            .Where(entry => string.IsNullOrWhiteSpace(entry.Why) || entry.Why.Length < 20)
            .Select(entry => entry.Shape)
            .ToList();
        await Assert.That(unexplained).IsEmpty()
            .Because($"a blind spot without a reason is not a record: {string.Join("; ", unexplained)}");
    }

}