using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.CSharp;
using Poly.Introspection;

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
                new PropertyDefinitionNode("Name", TypeReference.Of(typeof(string)))
            ],
            Methods: [
                new MethodDefinitionNode(
                    "GetName",
                    TypeReference.Of(typeof(string)),
                    Body: memberAccess)
            ]);

        var analysis = new AnalyzerBuilder()
            .AddAnalyzer(new TypeDefinitionNodeAnalyzer())
            .UseThisReferenceContext()
            .UseTypeAndMemberResolver()
            .Build()
            .Analyze(typeNode);
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

        var analysis = new AnalyzerBuilder()
            .AddAnalyzer(new TypeDefinitionNodeAnalyzer())
            .UseThisReferenceContext()
            .UseTypeAndMemberResolver()
            .Build()
            .Analyze(typeNode);
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
                    TypeReference.Of(typeof(string)),
                    Body: thisReference,
                    IsStatic: true)
            ]);

        var analysis = new AnalyzerBuilder()
            .AddAnalyzer(new TypeDefinitionNodeAnalyzer())
            .UseThisReferenceContext()
            .UseTypeAndMemberResolver()
            .Build()
            .Analyze(typeNode);
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
                new PropertyDefinitionNode("Name", TypeReference.Of(typeof(string))),
                new PropertyDefinitionNode(
                    "DisplayName",
                    TypeReference.Of(typeof(string)),
                    Getter: new PropertyGetterDefinitionNode(memberAccess))
            ]);

        var analysis = new AnalyzerBuilder()
            .AddAnalyzer(new TypeDefinitionNodeAnalyzer())
            .UseThisReferenceContext()
            .UseTypeAndMemberResolver()
            .Build()
            .Analyze(typeNode);
        var diagnostics = analysis.Diagnostics.Where(static diagnostic => diagnostic.Code == "TH0001").ToArray();
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
                    TypeReference.Of(typeof(string)),
                    Initializer: new PropertyInitializerDefinitionNode(thisReference),
                    IsStatic: true)
            ]);

        var analysis = new AnalyzerBuilder()
            .AddAnalyzer(new TypeDefinitionNodeAnalyzer())
            .UseThisReferenceContext()
            .UseTypeAndMemberResolver()
            .Build()
            .Analyze(typeNode);
        var diagnostics = analysis.Diagnostics.Where(static diagnostic => diagnostic.Code == "TH0001").ToArray();
        var resolvedType = analysis.GetResolvedType(thisReference);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(resolvedType).IsNotNull();
        await Assert.That(resolvedType!.FullName).IsEqualTo("Sample.Widget");
    }

    [Test]
    public async Task Analyze_RootProgramThisReference_IsLegal() {
        var thisReference = new ThisReference();

        var analysis = new AnalyzerBuilder()
            .UseThisReferenceContext()
            .UseTypeAndMemberResolver()
            .Build()
            .Analyze(thisReference);

        await Assert.That(analysis.Diagnostics.Any(static d => d.Code == "TH0001")).IsFalse();
        await Assert.That(analysis.GetResolvedType(thisReference)).IsNull();
    }
}