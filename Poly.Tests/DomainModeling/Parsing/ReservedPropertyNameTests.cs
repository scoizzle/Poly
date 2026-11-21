using Poly.Grammar;

namespace Poly.Tests.DomainModeling.Parsing;

/// <summary>
/// A property named after a DSL keyword (<c>from</c>, <c>to</c>, …) lexes as that keyword token, not an
/// <c>Identifier</c>, so the entity-body dispatch cannot match it. The refusal names the word as reserved and
/// the rename, at the word's position, instead of "Expected property, stage, action, or policy" (#549).
/// </summary>
public sealed class ReservedPropertyNameTests {
    private static GrammarException ParseEntityError(string member) {
        var poly = $"domain D\nA: entity {{\n  {member}\n}}";
        try {
            new PolyDslParser(poly).Parse();
        }
        catch (GrammarException ex) {
            return ex;
        }

        throw new InvalidOperationException($"Expected a parse error for '{member}'.");
    }

    [Test]
    public async Task From_AsPropertyName_NamesTheReservedWordAtItsPosition() {
        var error = ParseEntityError("from: Text");

        await Assert.That(error.Detail).Contains("'from' is a reserved word");
        await Assert.That(error.Detail).Contains("'From'");
        await Assert.That(error.Line).IsEqualTo(3);
        await Assert.That(error.Column).IsEqualTo(3);
    }

    [Test]
    public async Task To_AsPropertyName_NamesTheReservedWordAtItsPosition() {
        var error = ParseEntityError("to: Text");

        await Assert.That(error.Detail).Contains("'to' is a reserved word");
        await Assert.That(error.Detail).Contains("'To'");
        await Assert.That(error.Line).IsEqualTo(3);
        await Assert.That(error.Column).IsEqualTo(3);
    }

    [Test]
    public async Task CapitalizedFrom_IsAPropertyName() {
        var changes = new PolyDslParser("""
            domain D
            A: entity {
              From: Text
            }
            """).Parse();

        var property = changes.OfType<Poly.DomainModeling.Evolution.AddPropertyToEntityChange>().Single();
        await Assert.That(property.Property.Name).IsEqualTo("From");
    }

    [Test]
    public async Task NonKeywordWord_KeepsTheGenericMembershipMessage() {
        var error = ParseEntityError("5");

        await Assert.That(error.Detail).Contains("Expected property, stage, action, or policy, got '5'");
        await Assert.That(error.Line).IsEqualTo(3);
        await Assert.That(error.Column).IsEqualTo(3);
    }
}