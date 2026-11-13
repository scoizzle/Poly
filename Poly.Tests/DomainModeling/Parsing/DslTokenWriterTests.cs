namespace Poly.Tests.DomainModeling.Parsing;

// ─── DslTokenWriter: the inverse of DslTokenReader.SkipWhitespaceAndComments ──
// A space goes between two word tokens and after ':' before a word; punctuation
// attaches, except for the G-27 rules (comma, `)` before a word, braces, `action (` and `if (`).
// Binders never emit spaces — the writer owns separators.

public sealed class DslTokenWriterTests {
    [Test]
    public async Task DslTokenWriter_EntityHeader_InsertsSpaceAfterColon() {
        var writer = new DslTokenWriter();
        writer.Write(DslTokenKind.Identifier, "Order");
        writer.Write(DslTokenKind.Colon);
        writer.Write(DslTokenKind.Entity);

        await Assert.That(writer.ToText()).IsEqualTo("Order: entity");
    }

    [Test]
    public async Task DslTokenWriter_TwoKeywords_InsertsSpace() {
        var writer = new DslTokenWriter();
        writer.Write(DslTokenKind.Assign);
        writer.Write(DslTokenKind.To);

        await Assert.That(writer.ToText()).IsEqualTo("assign to");
    }

    [Test]
    public async Task DslTokenWriter_CallArguments_AttachToTheParens_AndSpaceAfterTheComma() {
        var writer = new DslTokenWriter();
        writer.Write(DslTokenKind.Identifier, "range");
        writer.Write(DslTokenKind.LParen);
        writer.Write(DslTokenKind.Number, "0");
        writer.Write(DslTokenKind.Comma);
        writer.Write(DslTokenKind.Number, "100");
        writer.Write(DslTokenKind.RParen);

        // G-27: a space after a comma before a word (the printed language is `range(0.00, 100.00)`).
        await Assert.That(writer.ToText()).IsEqualTo("range(0, 100)");
    }

    [Test]
    public async Task DslTokenWriter_StringLiteral_InsertsSpaceBetweenWords() {
        var writer = new DslTokenWriter();
        writer.Write(DslTokenKind.StringLiteral, "hello world");
        writer.Write(DslTokenKind.StringLiteral, "bye");

        await Assert.That(writer.ToText()).IsEqualTo("hello world bye");
    }

    // ── G-27 spacing rules; names are raw text (the default kind), as a table row writes a fill ──

    private static void Name(DslTokenWriter writer, string text) => writer.Write(default, text);

    [Test]
    public async Task DslTokenWriter_KeyList_SpacesAfterComma() {
        var writer = new DslTokenWriter();
        Name(writer, "key");
        writer.Write(DslTokenKind.LParen);
        Name(writer, "A");
        writer.Write(DslTokenKind.Comma);
        Name(writer, "B");
        writer.Write(DslTokenKind.RParen);

        await Assert.That(writer.ToText()).IsEqualTo("key(A, B)");
    }

    [Test]
    public async Task DslTokenWriter_ActionHeader_SpacesBeforeParenAndAroundArrow() {
        var writer = new DslTokenWriter();
        writer.Write(DslTokenKind.Public);
        writer.Write(DslTokenKind.Action);
        Name(writer, "Place");
        writer.Write(DslTokenKind.LParen);
        Name(writer, "p");
        writer.Write(DslTokenKind.Colon);
        Name(writer, "T");
        writer.Write(DslTokenKind.Comma);
        Name(writer, "q");
        writer.Write(DslTokenKind.Colon);
        Name(writer, "U");
        writer.Write(DslTokenKind.RParen);
        writer.Write(DslTokenKind.Arrow);
        Name(writer, "R");

        await Assert.That(writer.ToText()).IsEqualTo("public action Place (p: T, q: U) -> R");
    }

    [Test]
    public async Task DslTokenWriter_StageActionHeader_SpacesAfterTheActionKeyword() {
        var writer = new DslTokenWriter();
        Name(writer, "Submit");
        writer.Write(DslTokenKind.Colon);
        writer.Write(DslTokenKind.Action);
        writer.Write(DslTokenKind.LParen);
        Name(writer, "p");
        writer.Write(DslTokenKind.Colon);
        Name(writer, "T");
        writer.Write(DslTokenKind.RParen);
        writer.Write(DslTokenKind.LBrace);

        await Assert.That(writer.ToText()).IsEqualTo("Submit: action (p: T) {");
    }

    [Test]
    public async Task DslTokenWriter_Initial_Invoke_CallHeads_Attach() {
        var initial = new DslTokenWriter();
        Name(initial, "initial");
        initial.Write(DslTokenKind.LParen);
        Name(initial, "S");
        initial.Write(DslTokenKind.RParen);

        var invoke = new DslTokenWriter();
        invoke.Write(DslTokenKind.Invoke);
        Name(invoke, "Rel");
        invoke.Write(DslTokenKind.Dot);
        Name(invoke, "Act");
        invoke.Write(DslTokenKind.LParen);
        Name(invoke, "p");
        invoke.Write(DslTokenKind.Colon);
        Name(invoke, "e");
        invoke.Write(DslTokenKind.RParen);

        await Assert.That(initial.ToText()).IsEqualTo("initial(S)");
        await Assert.That(invoke.ToText()).IsEqualTo("invoke Rel.Act(p: e)");
    }

    [Test]
    public async Task DslTokenWriter_Braces_SpaceOutsideAndInside_EmptyAttaches() {
        var open = new DslTokenWriter();
        Name(open, "Order");
        open.Write(DslTokenKind.Colon);
        open.Write(DslTokenKind.Entity);
        open.Write(DslTokenKind.LBrace);

        var inline = new DslTokenWriter();
        inline.Write(DslTokenKind.Policy);
        inline.Write(DslTokenKind.LBrace);
        Name(inline, "e");
        inline.Write(DslTokenKind.RBrace);

        var empty = new DslTokenWriter();
        empty.Write(DslTokenKind.Create);
        Name(empty, "X");
        empty.Write(DslTokenKind.LBrace);
        empty.Write(DslTokenKind.RBrace);

        var afterParen = new DslTokenWriter();
        afterParen.Write(DslTokenKind.RParen);
        afterParen.Write(DslTokenKind.LBrace);

        await Assert.That(open.ToText()).IsEqualTo("Order: entity {");
        await Assert.That(inline.ToText()).IsEqualTo("policy { e }");
        await Assert.That(empty.ToText()).IsEqualTo("create X {}");
        await Assert.That(afterParen.ToText()).IsEqualTo(") {");
    }

    [Test]
    public async Task DslTokenWriter_If_SpacesBeforeParen() {
        var head = new DslTokenWriter();
        head.Write(DslTokenKind.If);
        head.Write(DslTokenKind.LParen);
        Name(head, "c");
        head.Write(DslTokenKind.RParen);
        head.Write(DslTokenKind.LBrace);

        await Assert.That(head.ToText()).IsEqualTo("if (c) {");
    }
}