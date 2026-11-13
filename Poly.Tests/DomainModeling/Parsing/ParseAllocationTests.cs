using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Language;

namespace Poly.Tests.DomainModeling.Parsing;

/// <summary>
/// A deterministic allocation ceiling for one DSL parse, the benchmark's op
/// (<c>new PolyDslParser(text, session).Parse()</c> on <c>dsl/hotel</c>, session from
/// <c>DomainSession.ForSource(text, ExtensionCatalog.ProductAuthoring)</c>). BenchmarkDotNet's
/// <c>--job short</c> moves by hundreds of bytes per row between runs of unchanged code; this test does not,
/// because it counts the bytes the parse allocates on its own thread: the whole loop runs on the parse
/// stack (so <c>Resolve</c> runs inline), 30 warm-up parses settle JIT tiering and lazy statics, and the
/// result is the minimum of 5 measured parses. G-13a's done-when ("allocations do not rise") is read here;
/// G-13b keeps the test as a ratchet: lower the ceiling when a slice lowers the number, never raise it.
/// </summary>
public sealed class ParseAllocationTests {
    /// <summary>
    /// Measured once with this test body on the consumers before the G-13a sweep (commit <c>1ae2d948</c>,
    /// every consumer still reading <c>DslToken.Text</c>), Debug build, as the suite runs: 2,346,080 bytes in
    /// two runs. At G-13a's head (<c>9c3cf7a3</c> plus this test) the same body measures 2,345,208. G-28 lowered
    /// the ceiling: the matcher indexes a row's elements instead of boxing an enumerator per row it tries, and the top
    /// level no longer matches the visibility rule for every declaration. Measured 2,155,896 bytes on 2026-11-13 at
    /// <c>54067e82</c>; the ceiling is that plus 1,024 bytes of run-to-run noise headroom (G-13a saw 56), not a budget.
    /// </summary>
    private const long HotelParseBytesCeiling = 2_156_920;

    internal static long MeasureHotelParse() {
        var text = File.ReadAllText(Path.Combine(GoldenDomains.RepoRoot, "docs/probes/dogfood/hotel.poly"));
        var session = DomainSession.ForSource(text, ExtensionCatalog.ProductAuthoring);
        return DslLimits.RunOnParseStack(() => {
            for (var i = 0; i < 30; i++)
                _ = new PolyDslParser(text, session).Parse();
            var least = long.MaxValue;
            for (var i = 0; i < 5; i++) {
                var before = GC.GetAllocatedBytesForCurrentThread();
                var changes = new PolyDslParser(text, session).Parse();
                var after = GC.GetAllocatedBytesForCurrentThread();
                GC.KeepAlive(changes);
                least = Math.Min(least, after - before);
            }

            return least;
        });
    }

    [Test]
    public async Task Parse_Hotel_AllocatesNoMoreThanBeforeTheSweep() {
        var bytes = MeasureHotelParse();

        await Assert.That(bytes).IsGreaterThan(100_000);
        await Assert.That(bytes).IsLessThanOrEqualTo(HotelParseBytesCeiling);
    }
}