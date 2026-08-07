using Poly.DomainModeling.Parsing;
using Poly.Grammar;

namespace Poly.Tests.Grammar;

/// <summary>
/// Regression for the GI-3 dual-cursor bug: recursive-descent holds a head
/// token in <c>_current</c> while <see cref="Matcher{TKind}"/> peeks the
/// reader. Without <see cref="TokenReader{TKind}.Unread"/>, TryMatch starts
/// one token late and top-level entity dispatch fails after the domain header.
/// </summary>
public class GrammarDslParserDispatchTests {
    [Test]
    public async Task Parse_MultipleEntities_AfterDomainHeader_Succeeds() {
        var text = """
            domain RetCreate
            Customer: entity {
              Name: Text
              orders: many Order
              PlaceOrder: action -> Order {
                create in orders { Code: "O1" }
              }
              Active: stage {}
            }
            Order: entity {
              Code: Text
              Draft: stage {}
            }
            """;
        var changes = new PolyDslParser(text).Parse();
        await Assert.That(changes.Count).IsGreaterThan(3);
    }

    [Test]
    public async Task Unread_MakesHeadTokenVisibleToMatcher() {
        var reader = new DslTokenReader("Item: entity { }");
        var current = reader.Read(); // Identifier "Item" — head held outside reader
        await Assert.That(current.Kind).IsEqualTo(DslTokenKind.Identifier);

        // Without Unread, matcher would see ':' as Peek(1) and fail "top".
        reader.Unread(current);
        var matcher = new Matcher<DslTokenKind>(DslGrammar.Build(), reader);
        var match = matcher.TryMatch("top");
        await Assert.That(match?.PatternName).IsEqualTo("entity");

        // Restore dual-cursor head
        current = reader.Read();
        await Assert.That(current.Text).IsEqualTo("Item");
    }
}