using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

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
        for (var i = 0; i < plain.Tokens.Count; i++) {
            var expected = plain.Tokens[i];
            var actual = withBom.Tokens[i];
            await Assert.That(actual.Kind).IsEqualTo(expected.Kind);
            await Assert.That(actual.Text).IsEqualTo(expected.Text);
            await Assert.That(actual.Line).IsEqualTo(expected.Line);
            await Assert.That(actual.Col).IsEqualTo(expected.Col);
            await Assert.That(actual.Length).IsEqualTo(expected.Length);
            // Offsets stay relative to the original text (the BOM occupies index 0), so offset
            // consumers splice the caller's own string instead of landing one character early.
            await Assert.That(actual.Offset).IsEqualTo(expected.Offset + 1);
        }

        var first = withBom.Tokens[0];
        await Assert.That(first.Kind).IsEqualTo(DslTokenKind.Domain);
        await Assert.That(first.Line).IsEqualTo(1);
        await Assert.That(first.Col).IsEqualTo(1);
        await Assert.That(first.Offset).IsEqualTo(1);
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
        var define = new PolyCompiler(ProductCatalog.Model, new SessionOptions(ProductCatalog.Model, ExtensionCatalog.ProductAuthoring)) { DefaultDomainName = "Imported" }
            .Analyze(Bom + Document, "<workspace>");
        await Assert.That(define.Succeeded).IsTrue();
    }

    [Test]
    public async Task Edit_OnBomDocument_Add_SplicesAtTheRightPlace() {
        var outcome = PolyEdit.Apply(Bom + EditDocument, Edit(Add("A", "M: Number")));

        await Assert.That(outcome.Ok).IsTrue();
        await Assert.That(outcome.Text)
            .IsEqualTo(Bom + "domain B\n\nA: entity {\n  N: Text\n  M: Number\n}\n");
    }

    [Test]
    public async Task Edit_OnBomDocument_Remove_SplicesAtTheRightPlace() {
        var outcome = PolyEdit.Apply(Bom + EditDocument, Edit(Remove("A")));

        await Assert.That(outcome.Ok).IsTrue();
        await Assert.That(outcome.Text).IsEqualTo(Bom + "domain B\n\n");
    }

    [Test]
    public async Task Edit_OnBomDocument_Replace_SplicesAtTheRightPlace() {
        const string Replacement = "C: entity {\n  N: Text\n}";
        var outcome = PolyEdit.Apply(Bom + EditDocument, Edit(Replace("A", Replacement)));

        await Assert.That(outcome.Ok).IsTrue();
        await Assert.That(outcome.Text).IsEqualTo(Bom + "domain B\n\n" + Replacement + "\n");
    }

    [Test]
    public async Task Parse_BomOnlyDocument_BehavesLikeEmptyDocument() {
        var (emptyExit, emptyDiagnostics) = PolycCheck([]);
        var (bomExit, bomDiagnostics) = PolycCheck([0xEF, 0xBB, 0xBF]);

        await Assert.That(bomExit).IsEqualTo(emptyExit);
        await Assert.That(Summarize(bomDiagnostics)).IsEqualTo(Summarize(emptyDiagnostics));

        // poly_define hands the decoded document to this analyze path.
        var emptyDefine = DefineAnalyze("");
        var bomDefine = DefineAnalyze(Bom);
        await Assert.That(bomDefine.Succeeded).IsEqualTo(emptyDefine.Succeeded);
        await Assert.That(Summarize(bomDefine.Diagnostics)).IsEqualTo(Summarize(emptyDefine.Diagnostics));
    }

    private const string EditDocument = "domain B\n\nA: entity {\n  N: Text\n}\n";

    private static string Edit(params string[] ops) => "[" + string.Join(",", ops) + "]";

    private static string Add(string parent, string dsl) =>
        JsonSerializer.Serialize(new { op = "add", parent, dsl });

    private static string Remove(string path) => JsonSerializer.Serialize(new { op = "remove", path });

    private static string Replace(string path, string dsl) =>
        JsonSerializer.Serialize(new { op = "replace", path, dsl });

    private static (int Exit, IReadOnlyList<CompileDiagnostic> Diagnostics) PolycCheck(byte[] bytes) {
        var dir = PolycTestSupport.TempDir();
        var path = Path.Combine(dir, "doc.poly");
        File.WriteAllBytes(path, bytes);
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exit = PolycApp.Run(
            ["check", path, "--format", "json"], new StringReader(""), false, stdout, stderr);
        var report = JsonNode.Parse(stdout.ToString())!.AsArray();
        return (exit, report.Select(d => DiagnosticFormat.FromJson(d!)).ToList());
    }

    private static CompileResult DefineAnalyze(string text) =>
        new PolyCompiler(ProductCatalog.Model, new SessionOptions(ProductCatalog.Model, ExtensionCatalog.ProductAuthoring)) { DefaultDomainName = "Imported" }
            .Analyze(text, "<workspace>");

    private static string Summarize(IReadOnlyList<CompileDiagnostic> diagnostics) =>
        string.Join("; ", diagnostics.Select(d => $"{d.Code}:{d.Message}"));
}