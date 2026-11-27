using Poly.Compile;
using Poly.Grammar;
using Poly.Packs.Product;

namespace Poly.Tests.DomainModeling.Parsing;

/// <summary>
/// Refusals of unsupported constructs carry the line and column of the offending word and name the construct,
/// instead of an unpositioned <see cref="FormatException"/> or a bare "Expected LBrace" (#522).
/// </summary>
public sealed class UnsupportedConstructPositionTests {
    private static GrammarException Refusal(string document) {
        try {
            new PolyDslParser(document).Parse();
        }
        catch (FormatException ex) {
            if (ex is GrammarException positioned)
                return positioned;
            throw new InvalidOperationException($"The refusal is unpositioned: {ex.GetType().Name}: {ex.Message}");
        }

        throw new InvalidOperationException("Expected the document to be refused.");
    }

    [Test]
    public async Task Actor_AsKind_IsRefusedAtTheWord() {
        var error = Refusal("domain B\nX: actor { N: Text }");

        await Assert.That(error.Detail).Contains("'actor' is not supported");
        await Assert.That(error.Line).IsEqualTo(2);
        await Assert.That(error.Column).IsEqualTo(4);
    }

    [Test]
    public async Task Actor_AsType_IsRefusedAtTheWord() {
        var error = Refusal("domain B\nX: entity {\n  Who: actor\n}");

        await Assert.That(error.Detail).Contains("'actor' is not supported");
        await Assert.That(error.Detail).Contains("used as type for 'Who'");
        await Assert.That(error.Line).IsEqualTo(3);
        await Assert.That(error.Column).IsEqualTo(8);
    }

    [Test]
    public async Task Actor_AsEffect_IsRefusedAtTheWord() {
        var error = Refusal("domain B\nX: entity {\n  Go: action {\n    actor\n  }\n}");

        await Assert.That(error.Detail).Contains("'actor' is not supported");
        await Assert.That(error.Line).IsEqualTo(4);
        await Assert.That(error.Column).IsEqualTo(5);
    }

    [Test]
    public async Task EntityInheritance_IsRefusedAtTheBaseName() {
        var error = Refusal("domain B\nX: Base entity { }");

        await Assert.That(error.Detail).Contains("no longer supported");
        await Assert.That(error.Line).IsEqualTo(2);
        await Assert.That(error.Column).IsEqualTo(4);
    }

    [Test]
    public async Task PolicyExternal_NamesTheUnsupportedConstructAtTheWord() {
        const string document = "domain B\nX: entity {\n  N: Text\n  P: policy external\n}\n";
        var error = Refusal(document);

        await Assert.That(error.Detail).Contains("'policy external' is not supported");
        await Assert.That(error.Detail).Contains("policy {");
        await Assert.That(error.Line).IsEqualTo(4);
        await Assert.That(error.Column).IsEqualTo(document.Split('\n')[3].IndexOf("external", StringComparison.Ordinal) + 1);
    }

    /// <summary>The compiler door (<c>poly_define</c>) places the finding: <c>DSL0001</c> with the word's line and column.</summary>
    [Test]
    public async Task Compiler_PolicyExternal_IsAPlacedDsl0001AtTheWord() {
        const string document = "domain B\nX: entity {\n  N: Text\n  P: policy external\n}\n";
        var compiler = new PolyCompiler(ProductCatalog.Catalog,
            new SessionOptions(ProductCatalog.Catalog, ExtensionCatalog.ProductAuthoring));

        var diagnostic = compiler.Analyze(document, "unsupported.poly").Diagnostics.Single(d => d.IsError);

        await Assert.That(diagnostic.Code).IsEqualTo(DiagnosticCodes.Parse);
        await Assert.That(diagnostic.Placed).IsTrue();
        await Assert.That(diagnostic.Line).IsEqualTo(4);
        await Assert.That(diagnostic.Column).IsEqualTo(document.Split('\n')[3].IndexOf("external", StringComparison.Ordinal) + 1);
    }
}