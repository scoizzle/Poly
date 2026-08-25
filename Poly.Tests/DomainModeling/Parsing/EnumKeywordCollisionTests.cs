namespace Poly.Tests.DomainModeling.Parsing;

/// <summary>
/// Enum-member reserved-keyword rejection. An enum member named after a DSL keyword
/// (type keywords like `Number`/`Text`, or structural keywords like `Create`/`Stage`/
/// `In`) lexes as that keyword token, not an Identifier — without a clear error the
/// parser fails cryptically on the RBrace expect. These tests pin the readable
/// fail-closed message (review finding: the check previously covered only the 5 type
/// keywords; non-type keywords like `Create` hit the cryptic error).
/// </summary>
public sealed class EnumKeywordCollisionTests {
    private static string ParseEnumError(string members) {
        var poly = $"domain D\n\nColor: enum {{ {members} }}";
        try {
            new PolyDslParser(poly).Parse();
        }
        catch (FormatException ex) {
            return ex.Message;
        }
        throw new InvalidOperationException("Expected a FormatException for reserved keyword enum member.");
    }

    [Test]
    public async Task EnumMember_TypeKeyword_ReportsClearError() {
        // Original case: a member named after a primitive type keyword.
        var message = ParseEnumError("Number");
        await Assert.That(message).Contains("reserved keyword");
        await Assert.That(message).Contains("Number");
    }

    [Test]
    public async Task EnumMember_StructuralKeyword_ReportsClearError() {
        // Widen: non-type keywords lex as keyword tokens too (WordToKind is
        // case-sensitive: lowercase `create`/`in`/`stage`/`entry` collide; capitalized
        // `Create`/`Stage` are valid Identifiers). They must produce the same readable
        // error, not `Expected RBrace, got 'create'`.
        var message = ParseEnumError("create");
        await Assert.That(message).Contains("reserved keyword");
        await Assert.That(message).Contains("create");
    }

    [Test]
    public async Task EnumMember_CapitalizedKeywordIsValidIdentifier() {
        // WordToKind is case-sensitive — capitalized forms (Create/Stage/In) lex as
        // Identifiers and are legal enum members. Must not be rejected.
        var changes = new PolyDslParser("domain D\n\nColor: enum { Red, Create, Stage, In }").Parse();
        await Assert.That(changes).IsNotEmpty();
    }

    [Test]
    public async Task EnumMember_TrailingComma_StillParses() {
        // The widened check must not break valid trailing-comma enums.
        var changes = new PolyDslParser("domain D\n\nColor: enum { Red, Green, }").Parse();
        await Assert.That(changes).IsNotEmpty();
    }

    [Test]
    public async Task EnumMember_ValidIdentifierAfterComma_StillParses() {
        // Non-keyword members unaffected.
        var changes = new PolyDslParser("domain D\n\nColor: enum { Red, Green, Blue }").Parse();
        await Assert.That(changes).IsNotEmpty();
    }
}