using Poly.DomainModeling.V2.Core;

namespace Poly.Tests.DomainModeling.V2;

public class SemanticIdTests {
    [Test]
    public async Task DefaultConstructor_GeneratesNonEmptyValue()
    {
        var id = new SemanticId();

        await Assert.That(string.IsNullOrWhiteSpace(id.Value)).IsFalse();
    }

    [Test]
    public async Task DefaultConstructor_GeneratesUniqueValues()
    {
        var first = new SemanticId();
        var second = new SemanticId();

        await Assert.That(first).IsNotEqualTo(second);
    }

    [Test]
    public async Task Constructor_WithValidCustomValue_AcceptsValue()
    {
        var id = new SemanticId("CUSTOM_ID_001");

        await Assert.That(id.Value).IsEqualTo("CUSTOM_ID_001");
        await Assert.That(id.ToString()).IsEqualTo("CUSTOM_ID_001");
    }

    [Test]
    public async Task Constructor_WithWhitespace_Throws()
    {
        await Assert.That(() => new SemanticId("bad id")).Throws<ArgumentException>();
    }

    [Test]
    public async Task Equality_IsValueBased()
    {
        var left = new SemanticId("SAME_ID");
        var right = new SemanticId("SAME_ID");

        await Assert.That(left).IsEqualTo(right);
        await Assert.That(left.GetHashCode()).IsEqualTo(right.GetHashCode());
    }
}