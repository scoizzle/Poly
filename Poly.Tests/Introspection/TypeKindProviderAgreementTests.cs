using Poly.Analysis;
using Poly.Ast.Nodes;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.CSharp;
using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Tests.Introspection;

/// <summary>
/// The two providers agree on what a type is: each states the same shape facts for the same shape, no producer invents
/// one, and the writer fails closed on a declaration form it cannot print. A core attribute that depends on which
/// provider answered is the defect this file guards. (Formerly stated over the closed <c>TypeKind</c> enum.)
/// </summary>
public class TypeKindProviderAgreementTests {
    /// <summary>The shape facts a definition states, as one comparable line.</summary>
    private static string Shape(ITypeDefinition definition) => string.Join("+", new[] {
        definition.GetFact<Enumeration>() is not null ? "enumeration" : null,
        definition.GetFact<ValueSemantics>() is not null ? "value" : null,
        definition.GetFact<InterfaceContract>() is not null ? "interface" : null,
        definition.GetFact<DelegateSignature>() is not null ? "delegate" : null,
    }.Where(static fact => fact is not null));

    private static ITypeDefinition Clr(Type type) => ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(type);

    [Test]
    public async Task PrimitiveType_Boolean_ReportsBooleanShape() {
        var boolean = Clr(typeof(bool));

        await Assert.That(boolean.PrimitiveType).IsEqualTo(PrimitiveType.Boolean);
        await Assert.That(Shape(boolean)).IsEqualTo("value");
    }

    [Test]
    public async Task ClrProvider_Object_IsTheStructurePrimitive() {
        await Assert.That(typeof(object).GetPrimitiveType()).IsEqualTo(PrimitiveType.Structure);
    }

    [Test]
    public async Task ClrProvider_EveryPrimitiveItMaps_ReportsItself() {
        // The vocabulary names what a host may have, so only the primitives the CLR maps are this provider's (I6);
        // the protocol primitives are pinned as having no CLR type by the sibling test below.
        foreach (var primitive in Enum.GetValues<PrimitiveType>().Where(static id => id.GetClrType() is not null)) {
            var definition = Clr(primitive.GetClrType()!);

            await Assert.That(definition.PrimitiveType).IsEqualTo(primitive)
                .Because($"'{primitive}' must report its own id");
            await Assert.That(definition.GetFact<Enumeration>()).IsNull();
        }
    }

    [Test]
    public async Task ClrProvider_HasNoTypeForAProtocolPrimitive() {
        foreach (var primitive in new[] { PrimitiveType.Undefined, PrimitiveType.Null, PrimitiveType.Symbol, PrimitiveType.BigInt }) {
            await Assert.That(primitive.GetClrType()).IsNull().Because($"'{primitive}' is not a CLR type");
            await Assert.That(ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(primitive)).IsNull()
                .Because($"the CLR provider must not pretend to have '{primitive}'");
        }
    }

    [Test]
    public async Task ClrProvider_StatesTheShapeOfEachSample() {
        (Type Sample, string Shape)[] samples = [
            (typeof(int), "value"), (typeof(string), ""), (typeof(object), ""), (typeof(DateTime), "value"),
            (typeof(Uri), ""), (typeof(KeyValuePair<int, int>), "value"), (typeof(IDisposable), "interface"),
            (typeof(DayOfWeek), "enumeration+value"), (typeof(Action), "delegate"),
        ];

        foreach (var (sample, shape) in samples)
            await Assert.That(Shape(Clr(sample))).IsEqualTo(shape).Because($"'{sample}' states its shape");
    }

    [Test]
    public async Task AstProvider_StatesTheShapeItsDeclarationFormDeclares() {
        await Assert.That(Shape(Resolve(new TypeDefinitionNode("Plain", Namespace: "Sample")))).IsEqualTo("");
        await Assert.That(Shape(Resolve(new TypeDefinitionNode("Status", Form: TypeDeclarationForm.Enum)))).IsEqualTo("enumeration");
        await Assert.That(Shape(Resolve(new TypeDefinitionNode("IWidget", Form: TypeDeclarationForm.Interface)))).IsEqualTo("interface");
        await Assert.That(Shape(Resolve(new TypeDefinitionNode("Money", Form: TypeDeclarationForm.Struct)))).IsEqualTo("value");
        await Assert.That(Resolve(new TypeDefinitionNode("Atomic", PrimitiveTypeId: PrimitiveType.Int32)).PrimitiveType)
            .IsEqualTo(PrimitiveType.Int32);
    }

    [Test]
    public async Task DeclarationForm_DefaultIsClass() {
        // A declaration that states no form is a class: the zero value must not claim a narrower shape.
        TypeDeclarationForm unstated = default;
        await Assert.That(unstated).IsEqualTo(TypeDeclarationForm.Class);
        await Assert.That(new TypeDefinitionNode("Plain").Form).IsEqualTo(TypeDeclarationForm.Class);
    }

    [Test]
    public async Task SyntheticAstTypes_StateNoShapeFact() {
        ITypeDefinition collection = new AstCollectionTypeDefinition(Clr(typeof(int)), CollectionKind.List);
        ITypeDefinition parameter = new AstGenericParameterTypeDefinition(
            new TestParameter("T"), collection);

        await Assert.That(Shape(collection)).IsEqualTo("");
        await Assert.That(collection.PrimitiveType).IsNull();
        await Assert.That(Shape(parameter)).IsEqualTo("");
        await Assert.That(parameter.PrimitiveType).IsNull();
    }

    [Test]
    public async Task Writer_StructForm_Throws() {
        var node = new TypeDefinitionNode("Broken", Form: TypeDeclarationForm.Struct);

        await Assert.That(() => new CSharpGenerator().Generate(node))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task GetElementType_NoArguments_OnNonCollectionIndexer_ReturnsNull() {
        var indexerClass = ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(typeof(ClrTypeIndexerTests.MultiParamIndexerClass));

        await Assert.That(indexerClass.GetElementType()).IsNull();
    }

    private static ITypeDefinition Resolve(TypeDefinitionNode node) {
        var context = AnalysisContext.CreateDefault();
        new TypeDefinitionNodeAnalyzer().Analyze(context, node);
        return context.TypeDefinitions.GetTypeDefinition(node.Name)!;
    }

    private sealed class TestParameter(string name) : IParameter {
        public int Position => 0;
        public string Name => name;
        public ITypeDefinition ParameterTypeDefinition => ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(typeof(object));
        public bool IsOptional => false;
        public object? DefaultValue => null;
    }
}