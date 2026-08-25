using Poly.Introspection;

namespace Poly.Tests.Interpretation;

public class PropertyDefinitionNodeTests {
    [Test]
    public async Task PropertyDefinitionNode_DefaultValue_NormalizesToInitializer() {
        var defaultValue = new Constant("alpha");
        var property = new PropertyDefinitionNode(
            "Name",
            new PrimitiveTypeReference(PrimitiveType.String),
            DefaultValue: defaultValue);

        await Assert.That(property.DefaultValue).IsSameReferenceAs(defaultValue);
        await Assert.That(property.Initializer).IsNotNull();
        await Assert.That(property.Initializer!.Value).IsSameReferenceAs(defaultValue);
        await Assert.That(property.ToString()).Contains("= alpha");
    }
}