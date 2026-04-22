using Poly.Interpretation.AbstractSyntaxTree;
using Poly.Interpretation.AbstractSyntaxTree.TypeDefinitions;
using Poly.Interpretation.Analysis;
using Poly.Introspection;

namespace Poly.Tests.Interpretation;

public class TypeDefinitionNodeAnalyzerTests {
    [Test]
    public async Task Analyze_TypeDefinitionNode_MapsAstMembersAndRelationships() {
        var baseType = new TypeDefinitionNode("Base", "Sample");
        var interfaceType = new TypeDefinitionNode("IWidget", "Sample");
        var subjectType = new TypeDefinitionNode(
            "Widget",
            "Sample",
            Properties: [
                new PropertyDefinitionNode(
                    "Item",
                    new PrimitiveTypeReference(PrimitiveType.Int32),
                    IndexParameters: [new Parameter("index", new PrimitiveTypeReference(PrimitiveType.Int32))])
            ],
            Methods: [
                new MethodDefinitionNode(
                    "Format",
                    new PrimitiveTypeReference(PrimitiveType.String),
                    Parameters: [
                        new Parameter("value", new PrimitiveTypeReference(PrimitiveType.String)),
                        new Parameter("count", new PrimitiveTypeReference(PrimitiveType.Int32), new Constant(3))
                    ])
            ],
            BaseType: new NamedTypeReference("Base", "Sample"),
            Interfaces: [new NamedTypeReference("IWidget", "Sample")],
            GenericParameters: [new Parameter("T")]);

        var analyzerPass = new TypeDefinitionNodeAnalyzer();
        var analyzer = new AnalyzerBuilder(analyzerPass);
        analyzer.AddAnalyzer(analyzerPass);

        var analysis = analyzer.Build().Analyze(new Block([baseType, interfaceType, subjectType]));
        var resolvedType = analysis.GetMetadata<TypeDefinitionMetadata>(subjectType)?.TypeDefinition;

        await Assert.That(resolvedType).IsNotNull();
        await Assert.That(resolvedType!.BaseType?.FullName).IsEqualTo("Sample.Base");
        await Assert.That(resolvedType.Interfaces.Select(static type => type.FullName).ToArray()).IsEquivalentTo(["Sample.IWidget"]);

        var genericParameter = resolvedType.GenericParameters.Single();
        await Assert.That(genericParameter.Name).IsEqualTo("T");
        await Assert.That(genericParameter.Position).IsEqualTo(0);
        await Assert.That(genericParameter.IsOptional).IsFalse();

        var indexer = resolvedType.Properties.Single();
        var indexParameter = indexer.Parameters!.Single();
        await Assert.That(indexParameter.Name).IsEqualTo("index");
        await Assert.That(indexParameter.Position).IsEqualTo(0);
        await Assert.That(indexParameter.ParameterTypeDefinition.GetRuntimeType()).IsEqualTo(typeof(int));

        var method = resolvedType.Methods.Single();
        var methodParameters = method.Parameters.ToArray();
        await Assert.That(methodParameters.Length).IsEqualTo(2);
        await Assert.That(methodParameters[0].Name).IsEqualTo("value");
        await Assert.That(methodParameters[0].ParameterTypeDefinition.GetRuntimeType()).IsEqualTo(typeof(string));
        await Assert.That(methodParameters[1].Name).IsEqualTo("count");
        await Assert.That(methodParameters[1].IsOptional).IsTrue();
        await Assert.That(methodParameters[1].DefaultValue).IsEqualTo(3);
        await Assert.That(methodParameters[1].ParameterTypeDefinition.GetRuntimeType()).IsEqualTo(typeof(int));
    }

    [Test]
    public async Task Analyze_TypeDefinitionNode_WithUnionProperty_ResolvesToExpectedRuntimeType() {
        var sameTypeUnion = new TypeDefinitionNode(
            "SameTypeUnionHolder",
            "Sample",
            Properties: [
                new PropertyDefinitionNode(
                    "Value",
                    new UnionTypeReference([
                        new PrimitiveTypeReference(PrimitiveType.Int32),
                        new PrimitiveTypeReference(PrimitiveType.Int32)
                    ]))
            ]);

        var mixedTypeUnion = new TypeDefinitionNode(
            "MixedTypeUnionHolder",
            "Sample",
            Properties: [
                new PropertyDefinitionNode(
                    "Value",
                    new UnionTypeReference([
                        new PrimitiveTypeReference(PrimitiveType.Int32),
                        new PrimitiveTypeReference(PrimitiveType.String)
                    ]))
            ]);

        var analyzerPass = new TypeDefinitionNodeAnalyzer();
        var analyzer = new AnalyzerBuilder(analyzerPass);
        analyzer.AddAnalyzer(analyzerPass);

        var analysis = analyzer.Build().Analyze(new Block([sameTypeUnion, mixedTypeUnion]));

        var sameTypeDefinition = analysis.GetMetadata<TypeDefinitionMetadata>(sameTypeUnion)?.TypeDefinition;
        var mixedTypeDefinition = analysis.GetMetadata<TypeDefinitionMetadata>(mixedTypeUnion)?.TypeDefinition;

        await Assert.That(sameTypeDefinition).IsNotNull();
        await Assert.That(mixedTypeDefinition).IsNotNull();

        var samePropertyType = sameTypeDefinition!.Properties.Single().MemberTypeDefinition.GetRuntimeType();
        var mixedPropertyType = mixedTypeDefinition!.Properties.Single().MemberTypeDefinition.GetRuntimeType();

        await Assert.That(samePropertyType).IsEqualTo(typeof(int));
        await Assert.That(mixedPropertyType).IsEqualTo(typeof(object));
    }
}