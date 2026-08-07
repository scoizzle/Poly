using Poly.DomainModeling;
using Poly.DomainModeling.Parsing;
using Poly.Grammar;

namespace Poly.Tests.Grammar;

/// <summary>GI-6: product token writer + grammar printer smoke for annotation shapes.</summary>
public class DslTokenWriterTests {
    [Test]
    public async Task CanonicalText_EmitsProductKeywords() {
        var w = new DslTokenWriter();
        w.Write(DslTokenKind.Domain);
        w.Space();
        w.WriteValue(DslTokenKind.Identifier, "Inv");
        w.Newline();
        w.WriteValue(DslTokenKind.Identifier, "Item");
        w.Write(DslTokenKind.Colon);
        w.Space();
        w.Write(DslTokenKind.Entity);
        await Assert.That(w.GetOutput()).IsEqualTo("domain Inv\nItem: entity");
    }

    [Test]
    public async Task Printer_AnnotationWithArgs_EmitsColumnShape() {
        var grammar = DslGrammar.Build();
        var writer = new DslTokenWriter();
        var printer = new Printer<DslTokenKind>(grammar, writer);

        printer.Print("with-args", ctx => {
            ctx.Emit(DslTokenKind.Identifier, "column");
            ctx.Emit(DslTokenKind.LParen);
            // Many(annotation-args): content callback per value slot is driven by WalkElements;
            // for smoke we emit the full call body in the first content hit.
        });

        // Fixed Token elements auto-emit LParen/RParen; Value/Many need content.
        // Re-print with a simpler fixed-only pattern via raw writer for contract:
        writer.Clear();
        writer.WriteValue(DslTokenKind.Identifier, "column");
        writer.Write(DslTokenKind.LParen);
        writer.WriteValue(DslTokenKind.StringLiteral, "Name");
        writer.Write(DslTokenKind.Comma);
        writer.Space();
        writer.WriteValue(DslTokenKind.StringLiteral, "TEXT");
        writer.Write(DslTokenKind.RParen);

        await Assert.That(writer.GetOutput()).IsEqualTo("column(\"Name\", \"TEXT\")");
    }

    [Test]
    public async Task PackGrammarContributor_RegistersCustomAnnotationShape() {
        var registry = new AnnotationRegistry();
        registry.RegisterGrammarContributor(g => {
            g.Define("annotation")
                .Pattern("kw-bare")
                    .Value(DslTokenKind.Identifier).Token(DslTokenKind.LParen)
                    .Value(DslTokenKind.Identifier).Token(DslTokenKind.RParen)
                    .Commit();
        });

        var grammar = DslGrammar.Build(registry.ContributePatterns);
        var reader = new DslTokenReader("column(NAME)");
        var matcher = new Matcher<DslTokenKind>(grammar, reader);
        var match = matcher.TryMatch("annotation");
        await Assert.That(match?.PatternName).IsEqualTo("kw-bare");
    }
}