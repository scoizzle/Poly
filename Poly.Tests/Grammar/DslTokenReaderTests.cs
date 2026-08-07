using Poly.DomainModeling.Parsing;
using Poly.Grammar;

namespace Poly.Tests.Grammar;

/// <summary>
/// GI-1 acceptance: <see cref="DslTokenReader"/> produces exactly the token
/// stream the legacy <see cref="PolyDslTokenizer"/> produces for representative
/// DSL inputs — same kinds, text, line, and column.
/// </summary>
public class DslTokenReaderTests {
    private static readonly string[] RepresentativeInputs = [
        // 1. Minimal domain
        """
        domain Inventory
        """,

        // 2. Entity with properties + constraints
        """
        domain Inventory
        Item: entity {
            Name: Text required length(1, 50) unique
            Qty: Number range(0, 100)
        }
        """,

        // 3. Stages + actions + effects + return type
        """
        domain Order
        Order: entity {
            Draft: stage {
                Submit: action (note: Text) -> Text require Adult {
                    transition to Confirmed
                }
            }
            Confirmed: stage { }
            Active: stage { }
            Cancel: action {
                delete
            }
        }
        """,

        // 4. Policies + expressions with comparisons and quantifiers
        """
        domain Shop
        Order: entity {
            Adult: policy { Age >= 18 }
            Expensive: policy { Total > 1000 }
            LargeActive: policy { (Total >= 500) and (Status is Active) }
            Submit: action require Expensive, not Adult {
                transition to Paid
            }
        }
        """,

        // 5. Subscriptions with quantifiers + peer binding
        """
        domain Shop
        Order: entity {
            Paid: stage {
                when any Tracks Active as t {
                    invoke Tracks.Approve(t: t.Qty)
                }
            }
        }
        """,

        // 6. Navigation properties (N1) incl. many/one/owned
        """
        domain Shop
        Customer: entity {
            orders: many Order
            primary: one OwnedAddress owned
            address: Address
        }
        Order: entity {
            owner: Customer
        }
        """,

        // 7. Enum types
        """
        domain App
        Color: enum { Red, Green, Blue }
        Paint: entity {
            color: Color
        }
        """,

        // 8. String escapes + comments + operators
        """
        domain D
        // leading comment
        Text: entity {
            // inline comment
            msg: Text pattern("a\"b\\c")
            N: Number
        }
        """,

        // 9. Conditional effects + create/invoke + filters
        """
        domain D
        Order: entity {
            Ship: action {
                if (Qty > 0) {
                    create Label { Name: "x" }
                } else {
                    delete
                }
            }
        }
        """,

        // 10. Entity-level when subscription
        """
        domain D
        Order: entity {
            when Tracks Active
            Track: entity {
                Qty: Number
            }
        }
        """,
    ];

    [Test]
    public async Task DslTokenReader_MatchesLegacyTokenizer_OnRepresentativeInputs() {
        foreach (var input in RepresentativeInputs) {
            var legacy = new PolyDslTokenizer(input);
            var modern = new DslTokenReader(input);

            while (true) {
                var expected = legacy.Next();
                var actual = modern.Read();

                // Compare kind by name: the legacy and modern kinds are distinct
                // enum types (TokenKind vs DslTokenKind) with identical values.
                await Assert.That(actual.Kind.ToString()).IsEqualTo(expected.Kind.ToString());
                await Assert.That(actual.Text).IsEqualTo(expected.Text);
                await Assert.That(actual.Line).IsEqualTo(expected.Line);
                await Assert.That(actual.Col).IsEqualTo(expected.Col);

                if (expected.Kind == LegacyTokenKind.EndOfFile) break;
            }
        }
    }

    [Test]
    public async Task DslTokenReader_ReportsUnexpectedCharacter_WithPosition() {
        var reader = new DslTokenReader("domain Inventory\n@");

        var ex = await Assert.ThrowsAsync<GrammarException>(() => {
            reader.Read(); // domain
            reader.Read(); // Inventory
            reader.Read(); // throws on '@'
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
}