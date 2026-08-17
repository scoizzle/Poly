using Poly.DomainModeling.Language;
using Poly.Grammar;

using JsonKind = Poly.Tests.Grammar.JsonKind;
using JsonToken = Poly.Tests.Grammar.JsonToken;
using JsonTokenizer = Poly.Tests.Grammar.JsonTokenizer;

namespace Poly.Tests.Grammar;

// ─── Printer: the engine's canonical-text emit surface (grammar-revision §2.6) ──
// Fixed kinds print canonical text; content positions (Value/Predicate/Any and
// Optional/Repeat/Ref/LeftAssoc/Balanced bodies) delegate to a handler callback.

public sealed class PrinterTests {
    // JSON canonical provider (fixed tokens only; values come from callbacks).
    private static string JsonCanonical(JsonKind kind) => kind switch {
        JsonKind.True => "true",
        JsonKind.False => "false",
        JsonKind.Null => "null",
        JsonKind.LBrace => "{",
        JsonKind.RBrace => "}",
        JsonKind.LBracket => "[",
        JsonKind.RBracket => "]",
        JsonKind.Colon => ":",
        JsonKind.Comma => ",",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, $"No canonical text for {kind}"),
    };

    private static Grammar<JsonToken, JsonKind> JsonGrammar() {
        return new GrammarBuilder<JsonToken, JsonKind>()
            .Define("value")
            .Pattern("string").Kind(JsonKind.String).Commit()
            .Pattern("number").Kind(JsonKind.Number).Commit()
            .Pattern("true").Kind(JsonKind.True).Commit()
            .Pattern("false").Kind(JsonKind.False).Commit()
            .Pattern("null").Kind(JsonKind.Null).Commit()
            .Pattern("object").Balanced(JsonKind.LBrace, JsonKind.RBrace).Commit()
            .Pattern("array").Balanced(JsonKind.LBracket, JsonKind.RBracket).Commit()
            .Build();
    }

    // ── Fixed kinds print canonical text ──

    [Test]
    public async Task Print_FixedKindPatterns_EmitsCanonicalText() {
        var printer = new Printer<JsonToken, JsonKind>(JsonGrammar(), JsonCanonical);

        await Assert.That(printer.Print("value", "true")).IsEqualTo("true");
        await Assert.That(printer.Print("value", "false")).IsEqualTo("false");
        await Assert.That(printer.Print("value", "null")).IsEqualTo("null");
    }

    // ── Content positions delegate to the handler callback ──

    [Test]
    public async Task Print_BalancedBody_DelegatesToCallback() {
        var printer = new Printer<JsonToken, JsonKind>(JsonGrammar(), JsonCanonical);

        var printed = printer.Print("value", "object", ctx => {
            ctx.Emit("\"name\"");
            ctx.Emit(JsonKind.Colon);
            ctx.Emit(" 42");
        });

        await Assert.That(printed).IsEqualTo("{\"name\": 42}");
    }

    // ── Value positions emit nothing without a callback (pattern skeleton) ──

    [Test]
    public async Task Print_WithoutCallback_EmitsSkeletonOnly() {
        var printer = new Printer<JsonToken, JsonKind>(JsonGrammar(), JsonCanonical);

        // The object pattern is Balanced only — fixed delimiters, empty body.
        await Assert.That(printer.Print("value", "object")).IsEqualTo("{}");
    }

    // ── Nested rule printing into the same output ──

    [Test]
    public async Task Print_NestedRule_PrintsIntoCurrentOutput() {
        var printer = new Printer<JsonToken, JsonKind>(JsonGrammar(), JsonCanonical);

        var printed = printer.Print("value", "object", ctx => {
            ctx.PrintRule("value", "true");
        });

        await Assert.That(printed).IsEqualTo("{true}");
    }

    // ── Fail closed: unknown pattern name throws ──

    [Test]
    public async Task Print_UnknownPattern_Throws() {
        var printer = new Printer<JsonToken, JsonKind>(JsonGrammar(), JsonCanonical);

        await Assert.That(() => printer.Print("value", "nope")).Throws<ArgumentException>();
    }

    // ── Product DSL: canonical map + structure skeleton with a value callback ──

    [Test]
    public async Task Print_DslCanonicalText_KeywordsAndPunctuation() {
        await Assert.That(DslGrammar.CanonicalText(DslTokenKind.Domain)).IsEqualTo("domain");
        await Assert.That(DslGrammar.CanonicalText(DslTokenKind.Entity)).IsEqualTo("entity");
        await Assert.That(DslGrammar.CanonicalText(DslTokenKind.Transition)).IsEqualTo("transition");
        await Assert.That(DslGrammar.CanonicalText(DslTokenKind.Colon)).IsEqualTo(":");
        await Assert.That(DslGrammar.CanonicalText(DslTokenKind.LBrace)).IsEqualTo("{");
    }

    [Test]
    public async Task Print_DslEntityPattern_SkeletonPlusValue() {
        var printer = new Printer<DslToken, DslTokenKind>(DslGrammar.Build(), DslGrammar.CanonicalText);

        // Without a callback, the leading Value(Identifier) emits nothing; fixed
        // tokens print verbatim (no invented spacing — callbacks own whitespace).
        await Assert.That(printer.Print("top", "entity")).IsEqualTo(":entity");

        // With a callback supplying the name at the Value position: "Order:entity".
        var named = printer.Print("top", "entity", ctx => ctx.Emit("Order"));
        await Assert.That(named).IsEqualTo("Order:entity");
    }

    // ── Writers: raw (skeleton honesty) vs language writer (product spacing) ──

    [Test]
    public async Task Printer_WithRawWriter_StillEmitsSkeleton() {
        // The raw writer appends canonical text verbatim (no inserted separators),
        // so engine-tests that pin the no-spaces skeleton stay honest.
        var printer = new Printer<DslToken, DslTokenKind>(
            DslGrammar.Build(), DslGrammar.CanonicalText,
            () => new StringTokenWriter<DslTokenKind>(DslGrammar.CanonicalText));

        await Assert.That(printer.Print("top", "entity")).IsEqualTo(":entity");

        var named = printer.Print("top", "entity", ctx => ctx.Emit("Order"));
        await Assert.That(named).IsEqualTo("Order:entity");
    }

    [Test]
    public async Task Printer_WithDslTokenWriter_InsertsProductSpacing() {
        // The language writer owns separators (the inverse of the reader's whitespace
        // skip): space after ':' before a word gives the product "Order: entity".
        var printer = new Printer<DslToken, DslTokenKind>(
            DslGrammar.Build(), DslGrammar.CanonicalText,
            () => new DslTokenWriter());

        var named = printer.Print("top", "entity", ctx => ctx.Emit("Order"));
        await Assert.That(named).IsEqualTo("Order: entity");
    }

    [Test]
    public async Task Print_DslCanonical_UnknownKind_Throws() {
        await Assert.That(() => DslGrammar.CanonicalText(DslTokenKind.EndOfFile))
            .Throws<ArgumentOutOfRangeException>();
    }
}