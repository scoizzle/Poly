using Poly.Interpretation.Analysis.Semantics;
using Poly.Introspection;
using Poly.Syntax.AbstractSyntaxTree;
using Poly.Syntax.AbstractSyntaxTree.TypeDefinitions;
using Poly.Syntax.Analysis;

namespace Poly.Tests.Interpretation;

public class ThisReferenceTests {
    [Test]
    public async Task Analyze_InstanceMethodBody_ThisReferenceResolvesToDeclaringTypeAndMembers() {
        var thisReference = new ThisReference();
        var memberAccess = new Member(thisReference, "Name");
        var typeNode = new TypeDefinitionNode(
            "Widget",
            "Sample",
            Properties: [
                new PropertyDefinitionNode("Name", new PrimitiveTypeReference(PrimitiveType.String))
            ],
            Methods: [
                new MethodDefinitionNode(
                    "GetName",
                    new PrimitiveTypeReference(PrimitiveType.String),
                    Body: memberAccess)
            ]);

        var analyzerPass = new TypeDefinitionNodeAnalyzer();
        var analyzer = new AnalyzerBuilder(analyzerPass);
        analyzer.AddAnalyzer(analyzerPass);
        analyzer.UseThisReferenceContext();
        analyzer.UseTypeResolver();
        analyzer.UseMemberResolver();

        var analysis = analyzer.Build().Analyze(typeNode);
        var resolvedType = analysis.GetResolvedType(thisReference);
        var resolvedMember = analysis.GetResolvedMember(memberAccess);

        await Assert.That(resolvedType).IsNotNull();
        await Assert.That(resolvedType!.FullName).IsEqualTo("Sample.Widget");
        await Assert.That(resolvedMember).IsNotNull();
        await Assert.That(resolvedMember!.Name).IsEqualTo("Name");
        await Assert.That(analysis.Diagnostics.Count(static diagnostic => diagnostic.Code == "TH0001")).IsEqualTo(0);
    }

    [Test]
    public async Task Analyze_ConstructorBody_ThisReferenceResolvesToDeclaringType() {
        var thisReference = new ThisReference();
        var typeNode = new TypeDefinitionNode(
            "Widget",
            "Sample",
            Constructors: [
                new ConstructorDefinitionNode(Body: thisReference)
            ]);

        var analyzerPass = new TypeDefinitionNodeAnalyzer();
        var analyzer = new AnalyzerBuilder(analyzerPass);
        analyzer.AddAnalyzer(analyzerPass);
        analyzer.UseThisReferenceContext();
        analyzer.UseTypeResolver();

        var analysis = analyzer.Build().Analyze(typeNode);
        var resolvedType = analysis.GetResolvedType(thisReference);

        await Assert.That(resolvedType).IsNotNull();
        await Assert.That(resolvedType!.FullName).IsEqualTo("Sample.Widget");
    }

    [Test]
    public async Task Analyze_StaticMethodBody_ThisReferenceProducesDiagnostic() {
        var thisReference = new ThisReference();
        var typeNode = new TypeDefinitionNode(
            "Widget",
            "Sample",
            Methods: [
                new MethodDefinitionNode(
                    "Bad",
                    new PrimitiveTypeReference(PrimitiveType.String),
                    Body: thisReference,
                    IsStatic: true)
            ]);

        var analyzerPass = new TypeDefinitionNodeAnalyzer();
        var analyzer = new AnalyzerBuilder(analyzerPass);
        analyzer.AddAnalyzer(analyzerPass);
        analyzer.UseThisReferenceContext();
        analyzer.UseTypeResolver();

        var analysis = analyzer.Build().Analyze(typeNode);
        var diagnostics = analysis.Diagnostics.Where(static diagnostic => diagnostic.Code == "TH0001").ToArray();
        var resolvedType = analysis.GetResolvedType(thisReference);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(resolvedType).IsNotNull();
        await Assert.That(resolvedType!.FullName).IsEqualTo("Sample.Widget");
    }

    [Test]
    public async Task Analyze_InstancePropertyGetter_ThisReferenceResolvesToDeclaringTypeAndMembers() {
        var thisReference = new ThisReference();
        var memberAccess = new Member(thisReference, "Name");
        var typeNode = new TypeDefinitionNode(
            "Widget",
            "Sample",
            Properties: [
                new PropertyDefinitionNode("Name", new PrimitiveTypeReference(PrimitiveType.String)),
                new PropertyDefinitionNode(
                    "DisplayName",
                    new PrimitiveTypeReference(PrimitiveType.String),
                    Getter: new PropertyGetterDefinitionNode(memberAccess))
            ]);

        var analyzerPass = new TypeDefinitionNodeAnalyzer();
        var analyzer = new AnalyzerBuilder(analyzerPass);
        analyzer.AddAnalyzer(analyzerPass);
        analyzer.UseThisReferenceContext();
        analyzer.UseTypeResolver();
        analyzer.UseMemberResolver();

        var analysis = analyzer.Build().Analyze(typeNode);
        var resolvedType = analysis.GetResolvedType(thisReference);
        var resolvedMember = analysis.GetResolvedMember(memberAccess);

        await Assert.That(resolvedType).IsNotNull();
        await Assert.That(resolvedType!.FullName).IsEqualTo("Sample.Widget");
        await Assert.That(resolvedMember).IsNotNull();
        await Assert.That(resolvedMember!.Name).IsEqualTo("Name");
    }

    [Test]
    public async Task Analyze_StaticPropertyInitializer_ThisReferenceProducesDiagnostic() {
        var thisReference = new ThisReference();
        var typeNode = new TypeDefinitionNode(
            "Widget",
            "Sample",
            Properties: [
                new PropertyDefinitionNode(
                    "Bad",
                    new PrimitiveTypeReference(PrimitiveType.String),
                    Initializer: new PropertyInitializerDefinitionNode(thisReference),
                    IsStatic: true)
            ]);

        var analyzerPass = new TypeDefinitionNodeAnalyzer();
        var analyzer = new AnalyzerBuilder(analyzerPass);
        analyzer.AddAnalyzer(analyzerPass);
        analyzer.UseThisReferenceContext();
        analyzer.UseTypeResolver();

        var analysis = analyzer.Build().Analyze(typeNode);
        var diagnostics = analysis.Diagnostics.Where(static diagnostic => diagnostic.Code == "TH0001").ToArray();
        var resolvedType = analysis.GetResolvedType(thisReference);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(resolvedType).IsNotNull();
        await Assert.That(resolvedType!.FullName).IsEqualTo("Sample.Widget");
    }

    [Test]
    public async Task Analyze_ThisReferenceOutsideMemberBody_ProducesDiagnostic() {
        var thisReference = new ThisReference();

        var analyzer = new AnalyzerBuilder()
            .UseThisReferenceContext()
            .UseTypeResolver()
            .Build();

        var analysis = analyzer.Analyze(thisReference);
        var diagnostics = analysis.Diagnostics.Where(static diagnostic => diagnostic.Code == "TH0002").ToArray();
        var resolvedType = analysis.GetResolvedType(thisReference);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(resolvedType).IsNull();
    }
}