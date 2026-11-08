using Poly.DomainModeling.Language;
using Poly.Grammar;

namespace Poly.Tests.Polyc;

public class PolycGrammarExceptionTests {
    [Test]
    public async Task Parse_UnexpectedCharacter_ThrowsGrammarExceptionWithPosition() {
        var text = "domain D\n\nE: entity {\n  a: Text @\n}\n";
        var ex = Assert.Throws<GrammarException>(() => DslSyntaxTree.Parse(text));
        await Assert.That(ex.Line).IsEqualTo(4);
        await Assert.That(ex.Column).IsEqualTo(11);
        await Assert.That(ex).IsAssignableTo<FormatException>();
        await Assert.That(ex.Message).Contains("Unexpected character");
    }

    [Test]
    public async Task Error_WithPosition_KeepsLegacyMessageAndDetail() {
        var ex = Assert.Throws<GrammarException>(() => DslSyntaxTree.Parse("domain D\nE: entity { a: Text # }"));
        await Assert.That(ex.Message).IsEqualTo($"{ex.Detail} (line {ex.Line}, col {ex.Column})");
    }
}