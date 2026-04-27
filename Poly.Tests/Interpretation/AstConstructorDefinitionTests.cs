using Poly.Interpretation.Analysis.Semantics;
using Poly.Introspection;
using Poly.Syntax.AbstractSyntaxTree;
using Poly.Syntax.AbstractSyntaxTree.TypeDefinitions;
using Poly.Syntax.Analysis;

namespace Poly.Tests.Interpretation;

public class AstConstructorDefinitionTests {
    [Test]
    public async Task Analyze_TypeDefinitionNode_MapsConstructorDefinitions() {
        var typeNode = new TypeDefinitionNode(
            "Widget",
            "Sample",
            Constructors: [
                new ConstructorDefinitionNode(),
                new ConstructorDefinitionNode([
                    new Parameter("name", new PrimitiveTypeReference(PrimitiveType.String)),
                    new Parameter("count", new PrimitiveTypeReference(PrimitiveType.Int32), new Constant(5))
                ]),
            ]);

        var analyzerPass = new TypeDefinitionNodeAnalyzer();
        var analyzer = new AnalyzerBuilder(analyzerPass);
        analyzer.AddAnalyzer(analyzerPass);

        var analysis = analyzer.Build().Analyze(typeNode);
        var resolvedType = analysis.GetMetadata<TypeDefinitionMetadata>(typeNode)?.TypeDefinition;

        await Assert.That(resolvedType).IsNotNull();
        await Assert.That(resolvedType!.Constructors.Count()).IsEqualTo(2);

        var constructor = resolvedType.Constructors.Single(static ctor => ctor.Parameters.Any());
        await Assert.That(constructor.Name).IsEqualTo("Widget");
        await Assert.That(constructor.MemberTypeDefinition).IsSameReferenceAs(resolvedType);

        var parameters = constructor.Parameters.ToArray();
        await Assert.That(parameters.Length).IsEqualTo(2);
        await Assert.That(parameters[0].Name).IsEqualTo("name");
        await Assert.That(parameters[0].ParameterTypeDefinition.GetRuntimeType()).IsEqualTo(typeof(string));
        await Assert.That(parameters[1].Name).IsEqualTo("count");
        await Assert.That(parameters[1].IsOptional).IsTrue();
        await Assert.That(parameters[1].DefaultValue).IsEqualTo(5);
        await Assert.That(parameters[1].ParameterTypeDefinition.GetRuntimeType()).IsEqualTo(typeof(int));
    }

    [Test]
    public async Task AnalyzeNode_New_ResolvesAstDefinedConstructor() {
        var typeNode = new TypeDefinitionNode(
            "Widget",
            "Sample",
            Constructors: [
                new ConstructorDefinitionNode(),
                new ConstructorDefinitionNode([
                    new Parameter("name", new PrimitiveTypeReference(PrimitiveType.String)),
                    new Parameter("count", new PrimitiveTypeReference(PrimitiveType.Int32), new Constant(2)),
                ])
            ]);
        var newNode = new New(new TypeReference("Sample.Widget"), new Constant("alpha"));
        var root = new Block([typeNode, newNode]);

        var analyzerPass = new TypeDefinitionNodeAnalyzer();
        var analyzer = new AnalyzerBuilder(analyzerPass);
        analyzer.AddAnalyzer(analyzerPass);
        analyzer.UseTypeResolver();
        analyzer.UseMemberResolver();

        var analysis = analyzer.Build().Analyze(root);
        var resolvedConstructor = analysis.GetResolvedMember(newNode) as ITypeConstructor;
        var resolvedType = analysis.GetResolvedType(newNode);

        await Assert.That(resolvedConstructor).IsNotNull();
        await Assert.That(resolvedConstructor!.DeclaringTypeDefinition.FullName).IsEqualTo("Sample.Widget");
        await Assert.That(resolvedConstructor.Parameters.Count()).IsEqualTo(2);
        await Assert.That(resolvedConstructor.Parameters.Last().IsOptional).IsTrue();
        await Assert.That(resolvedType).IsNotNull();
        await Assert.That(resolvedType!.FullName).IsEqualTo("Sample.Widget");
    }
}