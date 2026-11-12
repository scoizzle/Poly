using Poly.DomainModeling.Language;
using Poly.Grammar;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.Grammar;

/// <summary>
/// <see cref="SourceText"/> and <see cref="SpanToken{TKind}"/>: the source-position surface. The oracle is
/// <see cref="DslTokenReader"/>'s own line/column counters (only <c>'\n'</c> ends a line, a leading BOM is not a
/// document character), which G-12 deletes against these tests.
/// </summary>
public class SourceTextTests {
    private const string CrlfDomain = "domain A\r\nX: entity {\r\n  N: Text\r\n}\r\n";

    private static readonly string[] InlineTexts = [
        "", "a", "a\n", "a\r\nb", "﻿domain A", CrlfDomain,
    ];

    // The legacy rule replayed: counters start at 1:1 after the BOM; '\n' resets the column.
    private static (int Line, int Col) Legacy(string text, int offset) {
        var origin = text.Length > 0 && text[0] == '﻿' ? 1 : 0;
        int line = 1, col = 1;
        for (var pos = origin; pos < offset; pos++) {
            if (text[pos] == '\n') { line++; col = 1; }
            else { col++; }
        }
        return (line, col);
    }

    private static IEnumerable<string> CorpusTexts() =>
        GoldenDomains.All.Select(d => d.Poly).Concat(InlineTexts);

    [Test]
    public async Task GetLineColumn_MatchesLegacyLineColTracking() {
        var checkedTokens = 0;
        foreach (var text in CorpusTexts()) {
            var source = new SourceText(text);
            var reader = new DslTokenReader(text);
            for (var i = 0; ; i++) {
                var t = reader.Peek(i);
                await Assert.That(source.GetLineColumn(t.Offset)).IsEqualTo((t.Line, t.Col));
                checkedTokens++;
                if (t.Kind == DslTokenKind.EndOfFile) {
                    await Assert.That(source.GetLineColumn(text.Length)).IsEqualTo((t.Line, t.Col));
                    break;
                }
            }
            await Assert.That(source.GetLineColumn(0)).IsEqualTo((1, 1));
        }
        await Assert.That(checkedTokens).IsGreaterThan(1000);
    }

    [Test]
    public async Task GetLineColumn_EveryOffset_MatchesLegacyCounters() {
        string[] texts = [
            "", "a", "a\n", "a\r\nb", "a\rb", "a\tb", "// c\nx", "﻿\nx", "﻿domain A", CrlfDomain,
        ];
        foreach (var text in texts) {
            var source = new SourceText(text);
            for (var i = 0; i <= text.Length; i++)
                await Assert.That(source.GetLineColumn(i)).IsEqualTo(Legacy(text, i));
        }
    }

    [Test]
    public async Task GetLineColumn_BomText_OffsetsZeroAndOneAreBothOrigin() {
        var source = new SourceText("﻿domain A");
        await Assert.That(source.GetLineColumn(0)).IsEqualTo((1, 1));
        await Assert.That(source.GetLineColumn(1)).IsEqualTo((1, 1));
        await Assert.That(source.GetLineColumn(2)).IsEqualTo((1, 2));
    }

    [Test]
    public async Task Span_ReturnsTheExactSlice() {
        var text = "domain A\n\nTask: entity {\n  N: Text\n}\n";
        var source = new SourceText(text);
        var reader = new DslTokenReader(text);
        var task = reader.Peek(2);
        var token = new SpanToken<DslTokenKind>(task.Kind, task.Offset, task.Length);

        await Assert.That(source.Span(token).ToString()).IsEqualTo("Task");
    }

    [Test]
    public async Task Span_OutOfRange_Throws_AndNeverClamps() {
        var source = new SourceText("abcd");
        await Assert.That(() => source.Span(new SpanToken<DslTokenKind>(default, -1, 1))).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => source.Span(new SpanToken<DslTokenKind>(default, 1, -1))).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => source.Span(new SpanToken<DslTokenKind>(default, 3, 2))).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => source.Span(new SpanToken<DslTokenKind>(default, 5, 0))).Throws<ArgumentOutOfRangeException>();
        await Assert.That(source.Span(new SpanToken<DslTokenKind>(default, 4, 0)).Length).IsEqualTo(0);
    }

    [Test]
    public async Task GetLineColumn_OutOfRange_Throws() {
        var source = new SourceText("abc");
        await Assert.That(() => source.GetLineColumn(-1)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => source.GetLineColumn(4)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(source.GetLineColumn(3)).IsEqualTo((1, 4));
    }

    [Test]
    public async Task Constructor_NullText_Throws() =>
        await Assert.That(() => new SourceText(null!)).Throws<ArgumentNullException>();

    /// <summary>
    /// Allocation is measured, not inferred: <c>GetAllocatedBytesForCurrentThread</c> is a per-thread counter, so
    /// parallel tests cannot disturb it (no <c>[NotInParallel]</c>); each region is fully synchronous (no await,
    /// no Assert inside it), so TUnit cannot hop threads mid-measurement, and is warmed once first.
    /// </summary>
    [Test]
    public async Task Span_ExchangeAllocatesZeroBytes() {
        var source = new SourceText("domain A\n\nTask: entity {\n  N: Text\n}\n");
        var token = new SpanToken<DslTokenKind>(DslTokenKind.Identifier, 10, 4);
        var sink = 0;

        sink += source.Span(token).Length;
        sink += source.GetLineColumn(20).Line;

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++) sink += source.Span(token).Length;
        var spanBytes = GC.GetAllocatedBytesForCurrentThread() - before;

        before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++) sink += source.GetLineColumn(20).Col;
        var lineColBytes = GC.GetAllocatedBytesForCurrentThread() - before;

        await Assert.That(sink).IsGreaterThan(0);
        await Assert.That(spanBytes).IsEqualTo(0L);
        await Assert.That(lineColBytes).IsEqualTo(0L);
    }

    [Test]
    public async Task SpanToken_IsAValueWithTheKindStartLengthShape() {
        var a = new SpanToken<DslTokenKind>(DslTokenKind.Identifier, 3, 4);
        var b = new SpanToken<DslTokenKind>(DslTokenKind.Identifier, 3, 4);

        await Assert.That(a).IsEqualTo(b);
        await Assert.That(((IToken<DslTokenKind>)a).Kind).IsEqualTo(DslTokenKind.Identifier);
        await Assert.That((a.Start, a.Length)).IsEqualTo((3, 4));
        await Assert.That(System.Runtime.CompilerServices.Unsafe.SizeOf<SpanToken<DslTokenKind>>()).IsEqualTo(12);
    }
}