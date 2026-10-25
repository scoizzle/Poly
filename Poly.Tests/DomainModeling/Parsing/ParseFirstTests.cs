using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;

namespace Poly.Tests.DomainModeling.Parsing;

/// <summary>
/// parse-first: <see cref="DslSyntaxTree.Parse"/> reads only tokens and the
/// header ids; <see cref="DomainResolver"/> applies the loaded session's meaning.
/// </summary>
public sealed class ParseFirstTests {
    [Test]
    public async Task DslSyntaxTree_Parse_UsesTemporal_NoSession() {
        var tree = DslSyntaxTree.Parse("""
            domain Shop
            uses temporal
            Item: entity { Due: Date }
            """);

        await Assert.That(tree.Name).IsEqualTo("Shop");
        await Assert.That(tree.Uses).IsEquivalentTo(new[] { "temporal" });
        await Assert.That(tree.Tokens[^1].Kind).IsEqualTo(DslTokenKind.EndOfFile);
    }

    [Test]
    public async Task Resolve_DueDate_WithTemporalSession_IsProperty() {
        var tree = DslSyntaxTree.Parse("""
            domain Shop
            uses temporal
            Item: entity { Due: Date }
            """);
        var session = DomainSession.ForDocument(tree, []);
        var changes = new DomainResolver(tree, session).Resolve();

        var property = changes.OfType<AddPropertyToEntityChange>().Single(c => c.Property.Name == "Due");
        await Assert.That(property.Property.Type.TypeName).IsEqualTo("Date");
    }

    [Test]
    public async Task Resolve_Duration_WithTemporalSession_IsDateOperation() {
        var tree = DslSyntaxTree.Parse("""
            domain Shop
            uses temporal
            Item: entity {
                Overdue: policy { Now - 12 Days }
            }
            """);
        var session = DomainSession.ForDocument(tree, []);
        var changes = new DomainResolver(tree, session).Resolve();

        var policy = changes.OfType<AddPolicyToEntityChange>().Single(p => p.Policy.Name == "Overdue");
        await Assert.That(policy.Policy.Expression).IsTypeOf<DateOperation>();
    }

    [Test]
    public async Task Resolve_Duration_WithoutTemporalSession_Throws() {
        var tree = DslSyntaxTree.Parse("""
            domain Shop
            Item: entity {
                Overdue: policy { Now - 12 Days }
            }
            """);
        var session = DomainSession.ForDocument(tree, []);

        await Assert.That(() => new DomainResolver(tree, session).Resolve())
            .Throws<FormatException>()
            .WithMessageContaining("requires uses temporal");
    }

    [Test]
    public async Task Resolve_ColumnAnnotation_WithoutPack_Throws() {
        var tree = DslSyntaxTree.Parse("""
            domain Shop
            Item: entity { Code: Text column("CODE") }
            """);
        var session = DomainSession.ForDocument(tree, []);

        await Assert.That(() => new DomainResolver(tree, session).Resolve())
            .Throws<FormatException>()
            .WithMessageContaining("Unknown or unregistered annotation");
    }

    [Test]
    public async Task DslSyntaxTree_Parse_DuplicateUses_FailsClosed() {
        await Assert.That(() => DslSyntaxTree.Parse("""
            domain Shop
            uses temporal
            uses temporal
            """))
            .Throws<FormatException>()
            .WithMessageContaining("more than once");
    }
}
