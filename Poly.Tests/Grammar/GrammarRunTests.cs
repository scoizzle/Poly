using Poly.DomainModeling.Language;
using Poly.Grammar;

namespace Poly.Tests.Grammar;

/// <summary>
/// The Run element (G-27): one or more tokens that continue the token before them, decided by a host
/// delegate. Its first production consumer is the <c>uses/pinned</c> row, whose pin is the same-line,
/// adjacent token run that <see cref="DslSyntaxTree.TryReadUse"/> reads by hand: the row's Run must
/// consume exactly the tokens that reader consumes, on <see cref="UsesPinReadPins.Cases"/> and on
/// <see cref="MoreCases"/>.
/// </summary>
public sealed class GrammarRunTests {
    /// <summary>Ten more inputs beyond the twelve P2 pins (parity is against the hand reader, not a literal).</summary>
    private static readonly string[] MoreCases = [
        "uses http 1",
        "uses http 1.2",
        "uses http a.b.c",
        "uses http -1.5",
        "uses http v1 // a comment",
        "uses http v1,",
        "uses http \"x\".y",
        "uses http v1\n  .2",
        "uses http v1(2)",
        "uses http , v1",
    ];

    private static IEnumerable<string> AllCases => UsesPinReadPins.Cases.Select(c => c.Source).Concat(MoreCases);

    /// <summary>What the hand reader consumes after <c>uses id</c>, and what the <c>uses</c> rule's match consumes after it.</summary>
    private static (int Reader, int Row, string RowPattern, int Capture) Parity(string source) {
        var tokens = UsesPinReadPins.Tokens(source);
        DslSyntaxTree.TryReadUse(tokens, 0, out _, out _, out var next);
        var match = new Matcher<DslToken, DslTokenKind>(DslGrammar.Core, new ListTokenReader<DslToken, DslTokenKind>(
            tokens, kind => kind == DslTokenKind.EndOfFile)).TryMatch("uses");
        var capture = match!.Captures.TryGetValue("version", out var pin) ? pin.Count : 0;
        return (next - 2, match.Consumed - 2, match.PatternName, capture);
    }

    [Test]
    public async Task UsesRow_ConsumesExactlyTheTokensTryReadUseConsumes() {
        var expected = AllCases.Select(source => $"{UsesPinReadPins.Shown(source)}: {Parity(source).Reader}");
        var actual = AllCases.Select(source => $"{UsesPinReadPins.Shown(source)}: {Parity(source).Row}");

        await Assert.That(string.Join("\n", actual)).IsEqualTo(string.Join("\n", expected));
    }

    [Test]
    public async Task UsesRow_PinnedCaptureIsTheWholeRun_AndAStopFallsBackToTheIdRow() {
        var results = AllCases.Select(source => (Source: source, Result: Parity(source))).ToList();
        var pinned = results.Where(r => r.Result.Reader > 0).ToList();
        var stops = results.Where(r => r.Result.Reader == 0).ToList();

        await Assert.That(pinned.Count(r => r.Result.Reader > 1)).IsGreaterThanOrEqualTo(4);
        await Assert.That(stops.Count).IsGreaterThanOrEqualTo(4);
        await Assert.That(pinned.All(r => r.Result is { RowPattern: "pinned" } && r.Result.Capture == r.Result.Reader)).IsTrue();
        await Assert.That(stops.All(r => r.Result is { RowPattern: "id", Capture: 0 })).IsTrue();
    }

    [Test]
    public async Task UsesPinnedRow_HoldsOneRunAndNoVersionValue() {
        var row = DslGrammar.Core.GetPattern("uses", "pinned");

        await Assert.That(row.Elements.OfType<Run<DslToken, DslTokenKind>>().Count(r => r.Name == "version")).IsEqualTo(1);
        await Assert.That(row.Elements.OfType<Value<DslToken, DslTokenKind>>().Any(v => v.Name == "version")).IsFalse();
    }

    [Test]
    public async Task UsesPinnedRow_PrintsThePinUnchanged() {
        var printer = new Printer<DslToken, DslTokenKind>(DslGrammar.Core, DslGrammar.CanonicalText, () => new DslTokenWriter());

        var text = printer.Print("uses", "pinned", fills: Fills.Of(("id", "http"), ("version", "1.2.3")));

        await Assert.That(text).IsEqualTo("uses http 1.2.3");
    }

    // ── engine rules, on a test row ──

    private static Grammar<DslToken, DslTokenKind> Table() => new GrammarBuilder<DslToken, DslTokenKind>()
        .Define("tag")
            .Pattern("run").Kind(DslTokenKind.Uses).Run(DslGrammar.AdjacentSameLine, "rest").Commit()
        .Build();

    [Test]
    public async Task Run_ZeroTokens_FailsTheElement() {
        var matcher = new Matcher<DslToken, DslTokenKind>(Table(), new DslTokenReader("uses\nnext"));

        await Assert.That(matcher.TryMatch("tag")).IsNull();
    }

    [Test]
    public async Task Run_StopsAtAGap() {
        var match = new Matcher<DslToken, DslTokenKind>(Table(), new DslTokenReader("uses a.b c\nd")).TryMatch("tag");

        await Assert.That(match?.Consumed).IsEqualTo(4);
        await Assert.That(match!.Captures["rest"].Count).IsEqualTo(3);
    }

    [Test]
    public async Task Run_AsTheFirstElement_ThrowsAtCommit() {
        var error = Assert.Throws<InvalidOperationException>(() => new GrammarBuilder<DslToken, DslTokenKind>()
            .Define("tag").Pattern("bad").Run(DslGrammar.AdjacentSameLine, "rest").Commit());

        await Assert.That(error.Message).Contains("Run 'rest'");
        await Assert.That(error.Message).Contains("'tag/bad'");
    }

    [Test]
    public async Task Run_AfterOnlyOptionals_ThrowsAtCommit() {
        var error = Assert.Throws<InvalidOperationException>(() => new GrammarBuilder<DslToken, DslTokenKind>()
            .Define("tag").Pattern("bad").Optional(DslTokenKind.Uses).Run(DslGrammar.AdjacentSameLine, "rest").Commit());

        await Assert.That(error.Message).Contains("Run 'rest'");
    }

    [Test]
    public async Task Run_InsideOptional_ThrowsAtCommit() {
        var error = Assert.Throws<InvalidOperationException>(() => new GrammarBuilder<DslToken, DslTokenKind>()
            .Define("tag").Pattern("bad").Kind(DslTokenKind.Uses)
                .Optional(new Run<DslToken, DslTokenKind>(DslGrammar.AdjacentSameLine, "rest")).Commit());

        await Assert.That(error.Message).Contains("Run 'rest'");
        await Assert.That(error.Message).Contains("Optional");
    }
}
