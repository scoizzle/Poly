using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.CSharp;
using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Tests.Analysis;

/// <summary>
/// C#'s own declarations belong to the analysis layer that found them, not to the shared C# system (#998 T12c; owner
/// rulings 13 and 14 of docs/decisions/2026-11-19-a-type-system-owns-definitions-vocabulary-and-rules.md). A synthetic
/// collection follows its arguments: a declared argument makes it the layer's, a CLR one leaves it C#'s. A declared type
/// with no base takes the target's own top type, else none.
/// </summary>
public class DeclarationOwnershipTests {
    private static readonly Analyzer Analyzer = new AnalyzerBuilder()
        .UseTypeDefinitionNodeAnalyzer()
        .Build();

    private static AnalysisResult Analyze(Node node, ITypeSystem? target = null) =>
        Analyzer.Analyze(node, settings: target is null ? null : AnalysisSettings.Default.With<ITypeSystem>(target));

    private static ITypeDefinition DefinitionOf(AnalysisResult analysis, TypeDefinitionNode node) {
        var definition = analysis.GetMetadata<TypeDefinitionMetadata>(node)?.TypeDefinition;
        return definition ?? throw new InvalidOperationException($"No definition for '{node.Name}'.");
    }

    [Test]
    public async Task DeclaredType_IsOwnedByItsAnalysisLayer() {
        var declared = new TypeDefinitionNode("Order");
        var analysis = Analyze(declared);

        await Assert.That(DefinitionOf(analysis, declared).System).IsSameReferenceAs(analysis.TypeSystem)
            .Because("a declaration is the analysis' own type, not the shared C# system's");
        await Assert.That(analysis.TypeSystem).IsNotSameReferenceAs(Poly.Interpretation.CSharp.CSharpTypeSystem.Shared);
    }

    [Test]
    public async Task DeclaredCollection_IsTheLayers_ClrCollectionIsCSharps() {
        var order = new TypeDefinitionNode("Order");
        var shelf = new TypeDefinitionNode(
            "Shelf",
            Properties: [
                new PropertyDefinitionNode(
                    "Orders",
                    new NamedTypeReference("List", TypeArguments: [new NamedTypeReference("Order")])),
                new PropertyDefinitionNode(
                    "Counts",
                    new NamedTypeReference("List", TypeArguments: [TypeReference.To(typeof(int))]))
            ]);
        var analysis = Analyze(new CompilationUnitNode([], null, [order, shelf], null));
        var resolved = DefinitionOf(analysis, shelf);

        await Assert.That(resolved.Properties.Single(static property => property.Name == "Orders").MemberTypeDefinition.System)
            .IsSameReferenceAs(analysis.TypeSystem)
            .Because("List<Order> holds a declared type, so it is the analysis layer's");
        await Assert.That(resolved.Properties.Single(static property => property.Name == "Counts").MemberTypeDefinition.System)
            .IsSameReferenceAs(Poly.Interpretation.CSharp.CSharpTypeSystem.Shared)
            .Because("List<int> holds only CLR types, so it stays C#'s");
    }

    [Test]
    public async Task ClrCollection_IsTheSameTypeAcrossAnalyses_DeclaredTypeIsNot() {
        static (AnalysisResult Analysis, CompilationUnitNode Unit) AnalyzeShelf() {
            var order = new TypeDefinitionNode("Order");
            var shelf = new TypeDefinitionNode(
                "Shelf",
                Properties: [
                    new PropertyDefinitionNode(
                        "Orders",
                        new NamedTypeReference("List", TypeArguments: [new NamedTypeReference("Order")])),
                    new PropertyDefinitionNode(
                        "Counts",
                        new NamedTypeReference("List", TypeArguments: [TypeReference.To(typeof(int))]))
                ]);
            var unit = new CompilationUnitNode([], null, [order, shelf], null);
            return (Analyze(unit), unit);
        }

        var (first, firstUnit) = AnalyzeShelf();
        var (second, secondUnit) = AnalyzeShelf();
        var firstShelf = DefinitionOf(first, firstUnit.Types[1]);
        var secondShelf = DefinitionOf(second, secondUnit.Types[1]);

        await Assert.That(TypeDefinitionExtensions.IsSameType(
                firstShelf.Properties.Single(static property => property.Name == "Counts").MemberTypeDefinition,
                secondShelf.Properties.Single(static property => property.Name == "Counts").MemberTypeDefinition))
            .IsTrue().Because("List<int> is the one shared CLR collection in both analyses");
        await Assert.That(TypeDefinitionExtensions.IsSameType(
                DefinitionOf(first, firstUnit.Types[0]),
                DefinitionOf(second, secondUnit.Types[0])))
            .IsFalse().Because("each analysis declares its own Order");
    }

    [Test]
    public async Task SyntheticCollections_AreTheSameTypeWithinOneAnalysis() {
        var order = new TypeDefinitionNode("Order");
        var shelf = new TypeDefinitionNode(
            "Shelf",
            Properties: [
                new PropertyDefinitionNode(
                    "Orders",
                    new NamedTypeReference("List", TypeArguments: [new NamedTypeReference("Order")])),
                new PropertyDefinitionNode(
                    "More",
                    new NamedTypeReference("List", TypeArguments: [new NamedTypeReference("Order")]))
            ]);
        var analysis = Analyze(new CompilationUnitNode([], null, [order, shelf], null));
        var resolved = DefinitionOf(analysis, shelf);
        var properties = resolved.Properties.ToArray();

        await Assert.That(TypeDefinitionExtensions.IsSameType(
                properties[0].MemberTypeDefinition,
                properties[1].MemberTypeDefinition))
            .IsTrue().Because("two synthetic collections of one kind and element are one type, each resolve a fresh instance");
    }

    [Test]
    public async Task UntypedParameter_IsTheTargetsObject_ElseFailsClosed() {
        static TypeDefinitionNode Declared() => new(
            "Order",
            Methods: [
                new MethodDefinitionNode(
                    "Format",
                    TypeReference.To(typeof(string)),
                    Parameters: [new Parameter("value")])
            ]);

        var declared = Declared();
        var underCSharp = DefinitionOf(Analyze(declared), declared);
        await Assert.That(underCSharp.Methods.WithName("Format").Single().Parameters.Single().ParameterTypeDefinition.GetRuntimeType())
            .IsEqualTo(typeof(object)).Because("C#'s own object is the untyped parameter's type");

        var ledgerDeclared = Declared();
        var underLedger = DefinitionOf(Analyze(ledgerDeclared, new LedgerHost()), ledgerDeclared);
        await Assert.That(() => underLedger.Methods.WithName("Format").Single().Parameters.Single().ParameterTypeDefinition)
            .Throws<InvalidOperationException>()
            .WithMessageContaining("does not bind")
            .Because("the ledger binds no object, and a declaration invents no CLR one");
    }

    [Test]
    public async Task Declaration_UnderADomainTarget_TakesTheChainsObject() {
        // The domain's words are its own (Text, Number, …); object is not one, so its target, C#, answers it: a domain
        // session's declarations keep System.Object's members and an untyped parameter is object (#998 T12c).
        var domain = new Poly.DomainModeling.Meaning.DomainTypeSystem(
            new Poly.DomainModeling.Meaning.TypeMappingRegistry(), Poly.Interpretation.CSharp.CSharpTypeSystem.Shared);
        var declared = new TypeDefinitionNode(
            "Order",
            Methods: [
                new MethodDefinitionNode(
                    "Format",
                    TypeReference.To(typeof(string)),
                    Parameters: [new Parameter("value")])
            ]);
        var underDomain = DefinitionOf(Analyze(declared, domain), declared);

        await Assert.That(underDomain.BaseType).IsNull();
        await Assert.That(underDomain.Methods.WithName("ToString").Any()).IsTrue()
            .Because("the domain layers on C#, whose object a declaration with no base inherits");
        await Assert.That(underDomain.Methods.WithName("Format").Single().Parameters.Single().ParameterTypeDefinition.GetRuntimeType())
            .IsEqualTo(typeof(object));
    }

    [Test]
    public async Task DeclaredClassWithoutBase_InheritsTheTargetsTopType_ElseNothing() {
        var declared = new TypeDefinitionNode("Order");

        var underCSharp = DefinitionOf(Analyze(declared), declared);
        await Assert.That(underCSharp.BaseType).IsNull()
            .Because("a declaration with no base names none, under C# as before");
        await Assert.That(underCSharp.Methods.WithName("ToString").Any()).IsTrue()
            .Because("under C#, a class with no base has CLR object's members");

        var ledgerDeclared = new TypeDefinitionNode("Order");
        var underLedger = DefinitionOf(Analyze(ledgerDeclared, new LedgerHost()), ledgerDeclared);
        await Assert.That(underLedger.BaseType).IsNull();
        await Assert.That(underLedger.Members.Any()).IsFalse()
            .Because("the ledger binds no word for object, so no CLR object members are invented for its declaration");
    }
}