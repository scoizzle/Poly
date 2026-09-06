using Poly.Interpretation.Analysis.Semantics;
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

    [Test]
    public async Task Analyze_PropertyDefinition_InTypeDefinition_MapsProperty() {
        var property = new PropertyDefinitionNode(
            "Name",
            new PrimitiveTypeReference(PrimitiveType.String),
            DefaultValue: new Constant("alpha"));
        var typeNode = new TypeDefinitionNode("Widget", "Sample", Properties: [property]);
        var analysis = new AnalyzerBuilder()
            .AddAnalyzer(new TypeDefinitionNodeAnalyzer())
            .Build()
            .Analyze(typeNode);
        var td = analysis.GetMetadata<TypeDefinitionMetadata>(typeNode)?.TypeDefinition;
        await Assert.That(td).IsNotNull();
        await Assert.That(td!.Properties.Single().Name).IsEqualTo("Name");
    }
}
