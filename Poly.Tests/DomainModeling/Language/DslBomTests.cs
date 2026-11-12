using System.Text;

using Poly.Compile;
using Poly.DomainModeling.Language;
using Poly.Grammar;
using Poly.Packs.Product;
using Poly.Tests.Polyc;

using Polyc;

namespace Poly.Tests.DomainModeling.Language;

/// <summary>
/// A single leading UTF-8 BOM is an encoding artifact, not a character: the scanner skips it
/// without moving line/column or any token's source offset, so <c>polyc check</c> (whose file
/// reader strips the BOM) and <c>poly_define</c> (which receives the decoded string) accept the
/// same bytes. A BOM anywhere else is a positioned error that names the code point.
/// </summary>
public class DslBomTests {
    private const string Document = "domain B\nA: entity { N: Text }\n";
    private const string Bom = "\uFEFF";

    [Test]
    public async Task Parse_LeadingUtf8Bom_EqualsParseWithoutBom() {
        var plain = DslSyntaxTree.Parse(Document);
        var withBom = DslSyntaxTree.Parse(Bom + Document);

        await Assert.That(withBom.Name).IsEqualTo("B");
        await Assert.That(withBom.Tokens.Count).IsEqualTo(plain.Tokens.Count);
        for (var i = 0; i < plain.Tokens.Count; i++)
            await Assert.That(withBom.Tokens[i]).IsEqualTo(plain.Tokens[i]);

        var first = withBom.Tokens[0];
        await Assert.That(first.Kind).IsEqualTo(DslTokenKind.Domain);
        await Assert.That(first.Line).IsEqualTo(1);
        await Assert.That(first.Col).IsEqualTo(1);
        await Assert.That(first.Offset).IsEqualTo(0);
    }

    [Test]
    public async Task Parse_MidTextBom_ReportsU_FEFF_Positioned() {
        var ex = Assert.Throws<GrammarException>(() => DslSyntaxTree.Parse("domain B\nA: entity { N: Text" + Bom + " }\n"));

        await Assert.That(ex.Message).Contains("U+FEFF");
        await Assert.That(ex.Line).IsEqualTo(2);
        await Assert.That(ex.Column).IsEqualTo(20);
    }

    [Test]
    public async Task Parse_SecondBom_ReportsU_FEFF_Positioned() {
        var ex = Assert.Throws<GrammarException>(() => DslSyntaxTree.Parse(Bom + Bom + Document));

        await Assert.That(ex.Message).Contains("U+FEFF");
        await Assert.That(ex.Line).IsEqualTo(1);
        await Assert.That(ex.Column).IsEqualTo(1);
    }

    [Test]
    public async Task Parse_StrayZeroWidthSpace_ReportsU_200B_Positioned() {
        var ex = Assert.Throws<GrammarException>(() => DslSyntaxTree.Parse("domain B\nA: entity { N:\u200BText }\n"));

        await Assert.That(ex.Message).Contains("U+200B");
        await Assert.That(ex.Line).IsEqualTo(2);
        await Assert.That(ex.Column).IsEqualTo(15);
    }

    [Test]
    public async Task Parse_PrintableStrayCharacter_KeepsQuotedGlyph() {
        var ex = Assert.Throws<GrammarException>(() => DslSyntaxTree.Parse("domain B\nA: entity { N: Text @ }\n"));

        await Assert.That(ex.Message).Contains("'@'");
        await Assert.That(ex.Message).DoesNotContain("U+");
    }

    [Test]
    public async Task SameBomBytes_AcceptedByPolycCheckAndDefine() {
        var dir = PolycTestSupport.TempDir();
        var path = Path.Combine(dir, "bom.poly");
        File.WriteAllBytes(path, [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(Document)]);

        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exit = PolycApp.Run(["check", path], new StringReader(""), false, stdout, stderr);

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(stderr.ToString()).IsEqualTo("");

        // poly_define receives the decoded document, so it sees the leading BOM as U+FEFF.
        var define = new PolyCompiler(ProductCatalog.Model, "authoring", new CompileHooks { DefaultDomainName = "Imported" })
            .Analyze(Bom + Document, "<workspace>");
        await Assert.That(define.Succeeded).IsTrue();
    }
}