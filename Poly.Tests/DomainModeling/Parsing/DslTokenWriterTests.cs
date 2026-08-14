using Poly.DomainModeling.Parsing;

namespace Poly.Tests.DomainModeling.Parsing;

// ─── DslTokenWriter: the inverse of DslTokenReader.SkipWhitespaceAndComments ──
// A space goes between two word tokens and after ':' before a word; punctuation
// attaches. Binders never emit spaces — the writer owns separators.

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
    public async Task DslTokenWriter_Punctuation_Attaches() {
        var writer = new DslTokenWriter();
        writer.Write(DslTokenKind.Identifier, "range");
        writer.Write(DslTokenKind.LParen);
        writer.Write(DslTokenKind.Number, "0");
        writer.Write(DslTokenKind.Comma);
        writer.Write(DslTokenKind.Number, "100");
        writer.Write(DslTokenKind.RParen);

        await Assert.That(writer.ToText()).IsEqualTo("range(0,100)");
    }

    [Test]
    public async Task DslTokenWriter_StringLiteral_InsertsSpaceBetweenWords() {
        var writer = new DslTokenWriter();
        writer.Write(DslTokenKind.StringLiteral, "hello world");
        writer.Write(DslTokenKind.StringLiteral, "bye");

        await Assert.That(writer.ToText()).IsEqualTo("hello world bye");
    }
}