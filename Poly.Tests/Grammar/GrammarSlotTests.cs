using Poly.DomainModeling.Language;
using Poly.Grammar;

namespace Poly.Tests.Grammar;

/// <summary>
/// The Slot element (G-27): a zero-width print hole that ends a row. The reader matches the row's head and
/// stops at the Slot (the host reads the expression after it); the printer writes the fill, and the DSL
/// writer spaces it like a word (<c>to Price * 2</c>). The builder rejects a Slot that is not last and a
/// Slot inside an Optional. Test rows only: G-29 (<c>function … = &lt;expr&gt;</c>) and G-33 (<c>assign</c>,
/// <c>link</c>, <c>unlink</c>) are the first production consumers.
/// </summary>
public sealed class GrammarSlotTests {
    private static Grammar<DslToken, DslTokenKind> Table() => new GrammarBuilder<DslToken, DslTokenKind>()
        .Define("effect")
            .Pattern("assign")
                .Kind(DslTokenKind.Assign).Value(DslTokenKind.Identifier, "property").Kind(DslTokenKind.To)
                .Slot("expr").Commit()
        .Build();

    private static Printer<DslToken, DslTokenKind> DslPrinter() =>
        new(Table(), DslGrammar.CanonicalText, () => new DslTokenWriter());

    [Test]
    public async Task Slot_AssignRow_ConsumesTheHeadAndLeavesTheExpression() {
        var reader = new DslTokenReader("assign Total to Price * 2");
        var match = new Matcher<DslToken, DslTokenKind>(Table(), reader).TryMatch("effect");

        await Assert.That(match?.PatternName).IsEqualTo("assign");
        await Assert.That(match!.Consumed).IsEqualTo(3);
        await Assert.That(match.Pattern!.Elements[^1] is Slot<DslToken, DslTokenKind>).IsTrue();
        await Assert.That(match.Captures.Keys).IsEquivalentTo(["property"]);
        await Assert.That(reader.Peek(3).TextEquals("Price")).IsTrue();
    }

    [Test]
    public async Task Slot_AtEndOfInput_MatchesZeroWidth() {
        var match = new Matcher<DslToken, DslTokenKind>(Table(), new DslTokenReader("assign Total to")).TryMatch("effect");

        await Assert.That(match?.Consumed).IsEqualTo(3);
    }

    [Test]
    public async Task Slot_PrintsItsFill_AndTheWriterSpacesIt() {
        var text = DslPrinter().Print("effect", "assign", fills: Fills.Of(("property", "Total"), ("expr", "Price * 2")));

        await Assert.That(text).IsEqualTo("assign Total to Price * 2");
    }

    [Test]
    public async Task Slot_PrintedText_ReadsBackAsTheHeadThenTheExpression() {
        const string expression = "Price * 2";
        var text = DslPrinter().Print("effect", "assign", fills: Fills.Of(("property", "Total"), ("expr", expression)));
        var reader = new DslTokenReader(text);
        var match = new Matcher<DslToken, DslTokenKind>(Table(), reader).TryMatch("effect");

        await Assert.That(match?.Consumed).IsEqualTo(3);
        await Assert.That(text[reader.Peek(match!.Consumed).Start..]).IsEqualTo(expression);
    }

    [Test]
    public async Task Slot_PrintWithoutTheFill_Throws() {
        var printer = DslPrinter();

        var error = Assert.Throws<InvalidOperationException>(() =>
            printer.Print("effect", "assign", fills: Fills.Of(("property", "Total"))));

        await Assert.That(error.Message).IsEqualTo("No print fill for 'expr'.");
    }

    [Test]
    public async Task Slot_NotLast_ThrowsAtCommit() {
        var error = Assert.Throws<InvalidOperationException>(() => new GrammarBuilder<DslToken, DslTokenKind>()
            .Define("effect").Pattern("bad").Kind(DslTokenKind.Assign).Slot("expr").Kind(DslTokenKind.To).Commit());

        await Assert.That(error.Message).Contains("Slot 'expr'");
        await Assert.That(error.Message).Contains("'effect/bad'");
    }

    [Test]
    public async Task Slot_InsideOptional_ThrowsAtCommit() {
        var error = Assert.Throws<InvalidOperationException>(() => new GrammarBuilder<DslToken, DslTokenKind>()
            .Define("effect").Pattern("bad").Kind(DslTokenKind.Assign)
                .Optional(new Slot<DslToken, DslTokenKind>("expr")).Commit());

        await Assert.That(error.Message).Contains("Slot 'expr'");
        await Assert.That(error.Message).Contains("Optional");
    }

    [Test]
    public async Task Slot_NameSharedWithACapture_ThrowsAtCommit() {
        var error = Assert.Throws<InvalidOperationException>(() => new GrammarBuilder<DslToken, DslTokenKind>()
            .Define("effect").Pattern("bad").Kind(DslTokenKind.Assign).Value(DslTokenKind.Identifier, "expr")
                .Slot("expr").Commit());

        await Assert.That(error.Message).Contains("'expr'");
    }
}