using Poly.DomainModeling.Parsing;
using Poly.Grammar;

namespace Poly.Tests.Grammar;

/// <summary>
/// GI-1 / GI-7: <see cref="DslTokenReader"/> golden streams (legacy tokenizer removed).
/// </summary>
public class DslTokenReaderTests {
    [Test]
    public async Task DslTokenReader_MinimalDomain_KindsAndText() {
        var reader = new DslTokenReader("domain Inventory");
        await Assert.That(reader.Read().Kind).IsEqualTo(DslTokenKind.Domain);
        var name = reader.Read();
        await Assert.That(name.Kind).IsEqualTo(DslTokenKind.Identifier);
        await Assert.That(name.Text).IsEqualTo("Inventory");
        await Assert.That(reader.Read().Kind).IsEqualTo(DslTokenKind.EndOfFile);
    }

    [Test]
    public async Task DslTokenReader_EntitySurface_TokenizesKeywordsAndPunct() {
        var reader = new DslTokenReader("""
            domain D
            Item: entity {
              Name: Text required
              orders: many Order
            }
            """);

        var kinds = new List<DslTokenKind>();
        while (true) {
            var t = reader.Read();
            kinds.Add(t.Kind);
            if (t.Kind == DslTokenKind.EndOfFile) break;
        }

        await Assert.That(kinds).Contains(DslTokenKind.Domain);
        await Assert.That(kinds).Contains(DslTokenKind.Entity);
        await Assert.That(kinds).Contains(DslTokenKind.Text);
        await Assert.That(kinds).Contains(DslTokenKind.Required);
        await Assert.That(kinds).Contains(DslTokenKind.Many);
        await Assert.That(kinds).Contains(DslTokenKind.Colon);
        await Assert.That(kinds).Contains(DslTokenKind.LBrace);
    }

    [Test]
    public async Task DslTokenReader_TwoCharOpsAndStringEscapes() {
        var reader = new DslTokenReader("""x >= 1 and y != "a\"b" """);
        await Assert.That(reader.Read().Text).IsEqualTo("x");
        await Assert.That(reader.Read().Kind).IsEqualTo(DslTokenKind.Gte);
        await Assert.That(reader.Read().Text).IsEqualTo("1");
        await Assert.That(reader.Read().Kind).IsEqualTo(DslTokenKind.And);
        await Assert.That(reader.Read().Text).IsEqualTo("y");
        await Assert.That(reader.Read().Kind).IsEqualTo(DslTokenKind.Neq);
        var str = reader.Read();
        await Assert.That(str.Kind).IsEqualTo(DslTokenKind.StringLiteral);
        await Assert.That(str.Text).IsEqualTo("a\"b");
    }

    [Test]
    public async Task DslTokenReader_ReportsUnexpectedCharacter_WithPosition() {
        var reader = new DslTokenReader("domain Inventory\n@");

        var ex = await Assert.ThrowsAsync<GrammarException>(() => {
            reader.Read();
            reader.Read();
            reader.Read();
            return Task.CompletedTask;
        });

        await Assert.That(ex).IsNotNull();
        await Assert.That(ex!.Line).IsEqualTo(2);
        await Assert.That(ex.Column).IsEqualTo(1);
    }

    [Test]
    public async Task DslTokenReader_PeeksWithoutConsuming() {
        var reader = new DslTokenReader("domain X");

        var first = reader.Peek();
        var again = reader.Peek();
        await Assert.That(again).IsEqualTo(first);

        var read = reader.Read();
        await Assert.That(read).IsEqualTo(first);
    }

    [Test]
    public async Task DslTokenReader_IdempotentAcrossTwoReaders() {
        const string input = """
            domain Order
            Order: entity {
              Draft: stage {
                Submit: action -> Text { transition to Confirmed }
              }
            }
            """;
        var a = Drain(new DslTokenReader(input));
        var b = Drain(new DslTokenReader(input));
        await Assert.That(a.Count).IsEqualTo(b.Count);
        for (var i = 0; i < a.Count; i++) {
            await Assert.That(a[i].Kind).IsEqualTo(b[i].Kind);
            await Assert.That(a[i].Text).IsEqualTo(b[i].Text);
            await Assert.That(a[i].Line).IsEqualTo(b[i].Line);
            await Assert.That(a[i].Col).IsEqualTo(b[i].Col);
        }
    }

    private static List<Token<DslTokenKind>> Drain(DslTokenReader reader) {
        var list = new List<Token<DslTokenKind>>();
        while (true) {
            var t = reader.Read();
            list.Add(t);
            if (t.Kind == DslTokenKind.EndOfFile) break;
        }
        return list;
    }
}