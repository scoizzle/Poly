using Poly.DomainModeling.V2.Core;

namespace Poly.Tests.DomainModeling.V2;

public class BoundedContextTests {
    [Test]
    public async Task Constructor_ValidInput_CreatesBoundedContext()
    {
        var context = new BoundedContext(new SemanticId("CTX_1"), "Billing", "Billing context");

        await Assert.That(context.Name).IsEqualTo("Billing");
        await Assert.That(context.Description).IsEqualTo("Billing context");
    }

    [Test]
    public async Task Constructor_WhitespaceName_Throws()
    {
        await Assert.That(() => new BoundedContext(new SemanticId("CTX_2"), "   ")).Throws<ArgumentException>();
    }
}