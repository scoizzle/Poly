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
    public async Task BareImportedName_IsOneDefinition_AtADeclarationAndAnExpressionSite() {
        // #998: a unit's scope is one of the analysis' own sources, and every pass looks a name up against it rather
        // than reading a node bound once. That is the end state only if no pass sees another answer: the declaration's
        // member type resolves lazily, on first read, after the scope has registered its source, and the expression is
        // resolved by a later pass against the same scope.
        var memberType = new NamedTypeReference("StringBuilder");
        var created = new New(new NamedTypeReference("StringBuilder"));
        var holder = new TypeDefinitionNode("Holder", Properties: [new PropertyDefinitionNode("Text", memberType)]);
        var stringBuilder = Poly.Introspection.CommonLanguageRuntime.ClrTypeDefinitionRegistry.Shared
            .GetTypeDefinition(typeof(System.Text.StringBuilder));

        var analysis = Poly.Interpretation.Interpreter.Analyze(new CompilationUnitNode(["System.Text"], null, [holder], [created]));

        await Assert.That(analysis.HasErrors).IsFalse();
        await Assert.That(DefinitionOf(analysis, holder).Properties.Single().MemberTypeDefinition)
            .IsSameReferenceAs(stringBuilder).Because("the declaration's member is bound through the unit's using");
        await Assert.That(analysis.GetResolvedType(created.Type)).IsSameReferenceAs(stringBuilder)
            .Because("the expression site, resolved by a later pass, asks the same scope and gets the same definition");
    }

    [Test]
    [Arguments("class Action { }\nclass Holder { public Action<int> Callback { get; set; } }", "'Action' is declared with 0 type parameter(s) and cannot be used with 1 type argument(s)")]
    [Arguments("class Action { }\nclass Holder { public object M(Action<int> a) { return a; } }", "'Action' is declared with 0 type parameter(s) and cannot be used with 1 type argument(s)")]
    [Arguments("class Action { }\nclass Holder { public object M() { Action<int> a = null; return a; } }", "'Action' is declared with 0 type parameter(s) and cannot be used with 1 type argument(s)")]
    [Arguments("class Action { }\nclass Holder { public object M() { return new Action<int>(); } }", "'Action' is declared with 0 type parameter(s) and cannot be used with 1 type argument(s)")]
    [Arguments("class Box<T> { }\nclass Holder { public Box<int, int> Pair { get; set; } }", "'Box' is declared with 1 type parameter(s) and cannot be used with 2 type argument(s)")]
    public async Task GenericName_WithAnotherArityThanItsDeclaration_IsRefused(string source, string message) {
        // Owner, 2026-11-20: the unit's own declaration answers its name, so `Action<int>` beside a declared non-generic
        // `Action` is neither System.Action<int> nor the declaration with its argument dropped. C# refuses it (CS0308,
        // CS0305), and so does the analysis, at the reference.
        var analysis = Poly.Interpretation.Interpreter.Analyze(CSharpLanguage.Read(source));

        await Assert.That(analysis.Diagnostics.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(static diagnostic => diagnostic.Message)).Contains(message);
    }

    [Test]
    public async Task GenericName_WithAnotherArityThanItsDeclaration_IsReportedAtTheReference_AndIsNeverTheHostsGeneric() {
        var declared = new TypeDefinitionNode("Action");
        var reference = new NamedTypeReference("Action", TypeArguments: [TypeReference.To<int>()]);
        var holder = new TypeDefinitionNode("Holder", Properties: [new PropertyDefinitionNode("Callback", reference)]);

        var analysis = Poly.Interpretation.Interpreter.Analyze(new CompilationUnitNode([], null, [declared, holder], null));

        var error = analysis.Diagnostics.Single(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        await Assert.That(error.Message)
            .IsEqualTo("'Action' is declared with 0 type parameter(s) and cannot be used with 1 type argument(s)");
        await Assert.That(error.Node).IsSameReferenceAs(reference);
        // The error is the refusal. The reference keeps the declaration as its answer, so a later read of the member
        // has a type to read and the analysis ends with its diagnostic, not an exception; it is never the host's generic.
        await Assert.That(analysis.GetResolvedType(reference)).IsSameReferenceAs(DefinitionOf(analysis, declared));
    }

    [Test]
    [Arguments("class Action { }\nclass Holder { public Action<int> X { get; set; } public object M() { return this.X; } }")]
    [Arguments("class Action { }\nclass Holder { public Action<int> X { get; set; } }\nclass User { public object M() { var h = new Holder(); return h.X; } }")]
    [Arguments("class Action { }\nclass Holder { public object N(Action<int> a) { return a; } public object M() { return this.N(null); } }")]
    public async Task GenericName_WithAnotherArityThanItsDeclaration_IsRefusedByADiagnostic_WhereItsMemberIsUsed(string source) {
        // A use of the member reads its type. Refusing the reference by answering no type made that read throw out of the
        // analysis ("Type with name 'Action' not found"), and the positioned error was lost with it.
        var analysis = Poly.Interpretation.Interpreter.Analyze(CSharpLanguage.Read(source));

        await Assert.That(analysis.Diagnostics.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(static diagnostic => diagnostic.Message))
            .Contains("'Action' is declared with 0 type parameter(s) and cannot be used with 1 type argument(s)");
    }

    [Test]
    [Arguments("class Box<T> { }\nclass Holder { public Box<int> One { get; set; } }")]
    [Arguments("class Action { }\nclass Holder { public Action Plain { get; set; } public System.Action<int> Host { get; set; } }")]
    public async Task GenericName_MatchingItsDeclaration_OrQualifiedToTheHost_IsNotRefused(string source) {
        var analysis = Poly.Interpretation.Interpreter.Analyze(CSharpLanguage.Read(source));

        await Assert.That(analysis.Diagnostics.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(static diagnostic => diagnostic.Message)).IsEmpty();
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
    public async Task GenericName_IsLookedUpWithItsArity_NeverAsTheNonGenericTypeOfThatName() {
        // #998 S2: `Action<int>` and `Action` are different types, and `Dictionary` exists only with two arguments. A
        // generic name is asked with its arity, so the non-generic type of the same name never answers for it.
        var holder = new TypeDefinitionNode("Holder", Properties: [
            new PropertyDefinitionNode("Callback", new NamedTypeReference("Action", TypeArguments: [TypeReference.To<int>()])),
            new PropertyDefinitionNode("Plain", new NamedTypeReference("Action")),
            new PropertyDefinitionNode("Counts", new NamedTypeReference("Dictionary", TypeArguments: [TypeReference.To<string>(), TypeReference.To<int>()])),
        ]);

        var analysis = Poly.Interpretation.Interpreter.Analyze(new CompilationUnitNode([], null, [holder], null));
        var properties = DefinitionOf(analysis, holder).Properties.ToDictionary(static property => property.Name, static property => property.MemberTypeDefinition.GetRuntimeType());

        await Assert.That(properties["Callback"]).IsEqualTo(typeof(Action<int>));
        await Assert.That(properties["Plain"]).IsEqualTo(typeof(Action));
        await Assert.That(properties["Counts"]).IsEqualTo(typeof(Dictionary<string, int>));
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