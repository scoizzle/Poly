namespace Poly.Tests.DomainModeling.Parsing;

/// <summary>
/// A member line that begins with a reserved keyword (<c>policy</c>, <c>stage</c>,
/// <c>when</c>, …) lexes as that keyword token, so it lands on the generic
/// "Expected property, stage, action, or policy" error. Without a hint the message
/// just repeats the token back (analysis-coverage finding J1/S10). Pin the hint.
/// </summary>
public sealed class MemberKeywordCollisionTests {
    private static string ParseError(string entityBody) {
        var poly = $"domain D\n\nThing: entity {{\n{entityBody}\n}}";
        try {
            new PolyDslParser(poly).Parse();
        }
        catch (FormatException ex) {
            return ex.Message;
        }
        throw new InvalidOperationException("Expected a FormatException for the reserved member name.");
    }

    [Test]
    public async Task Member_ReservedKeyword_NamesTheReservedWord() {
        var message = ParseError("  Name: Text\n  policy: Text");
        await Assert.That(message).Contains("reserved word");
        await Assert.That(message).Contains("policy");
    }

    [Test]
    public async Task ValidEntity_StillParses() {
        var changes = new PolyDslParser("domain D\n\nThing: entity {\n  Name: Text\n  Count: Number\n}").Parse();
        await Assert.That(changes).IsNotEmpty();
    }
}