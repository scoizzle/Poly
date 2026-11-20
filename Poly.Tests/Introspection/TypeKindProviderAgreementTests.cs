using Poly.Analysis;
using Poly.Ast.Nodes;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.CSharp;
using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.Introspection;

/// <summary>
/// The two providers agree on what a type is: C#'s rule states the same shape facts (enum, value semantics) for the
/// same shape whichever provider built the definition (#998 T8), no producer invents one, and the writer fails closed
/// on a declaration form it cannot print. A core attribute that depends on which
/// provider answered is the defect this file guards. (Formerly stated over the closed <c>TypeKind</c> enum.)
/// </summary>
public class TypeKindProviderAgreementTests {
    /// <summary>The shape facts C#'s rule states for a definition, as one comparable line.</summary>
    private static string Shape(ITypeDefinition definition) => string.Join("+", new[] {
        Poly.Interpretation.CSharp.CSharpTypeSystem.Shared.IsEnumType(definition) ? "enumeration" : null,
        Poly.Interpretation.CSharp.CSharpTypeSystem.Shared.HasValueSemantics(definition) ? "value" : null,
    }.Where(static fact => fact is not null));

    private static ITypeDefinition Clr(Type type) => ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(type);

    [Test]
    public async Task PrimitiveType_Boolean_ReportsBooleanShape() {
        var boolean = Clr(typeof(bool));

        await Assert.That(CSharpTypeSystem.Shared.SimpleTypeOf(boolean)).IsEqualTo(SimpleType.Bool);
        await Assert.That(Shape(boolean)).IsEqualTo("value");
    }

    [Test]
    public async Task ClrProvider_Object_IsTheStructurePrimitive() {
        await Assert.That(ClrBuiltInRules.IsPrimitive(Clr(typeof(object)))).IsTrue();
        await Assert.That(CSharpTypeSystem.Shared.SimpleTypeOf(Clr(typeof(object)))).IsNull()
            .Because("object is no C# simple type; identity is what C# names it by");
    }

    [Test]
    public async Task ClrProvider_EveryPrimitiveItMaps_ReportsItself() {
        // The CLR host's own table: every entry is a CLR type, and its definition is a ground type that reports it.
        foreach (var type in ClrBuiltInTypes.All) {
            var definition = Clr(type);

            await Assert.That(definition.GetRuntimeTypeOrThrow()).IsEqualTo(type)
                .Because($"'{type}' must be its own definition");
            await Assert.That(Poly.Interpretation.CSharp.CSharpTypeSystem.Shared.IsEnumType(definition)).IsFalse();
            await Assert.That(ClrBuiltInRules.IsPrimitive(definition)).IsTrue().Because($"'{type}' is one of the CLR host's primitives");
        }
    }

    [Test]
    public async Task ClrProvider_StatesTheShapeOfEachSample() {
        (Type Sample, string Shape)[] samples = [
            (typeof(int), "value"), (typeof(string), ""), (typeof(object), ""), (typeof(DateTime), "value"),
            (typeof(Uri), ""), (typeof(KeyValuePair<int, int>), "value"), (typeof(IDisposable), ""),
            (typeof(DayOfWeek), "enumeration+value"), (typeof(System.Action), ""),
        ];

        foreach (var (sample, shape) in samples)
            await Assert.That(Shape(Clr(sample))).IsEqualTo(shape).Because($"'{sample}' states its shape");
    }

    [Test]
    public async Task AstProvider_StatesTheShapeItsDeclarationFormDeclares() {
        await Assert.That(Shape(Resolve(new TypeDefinitionNode("Plain", Namespace: "Sample")))).IsEqualTo("");
        await Assert.That(Shape(Resolve(new TypeDefinitionNode("Status", Form: TypeDeclarationForm.Enum)))).IsEqualTo("enumeration");
        await Assert.That(Shape(Resolve(new TypeDefinitionNode("IWidget", Form: TypeDeclarationForm.Interface)))).IsEqualTo("");
        await Assert.That(Shape(Resolve(new TypeDefinitionNode("Money", Form: TypeDeclarationForm.Struct)))).IsEqualTo("value");
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
        await Assert.That(ClrBuiltInRules.IsPrimitive(collection)).IsFalse();
        await Assert.That(Shape(parameter)).IsEqualTo("");
        await Assert.That(ClrBuiltInRules.IsPrimitive(parameter)).IsFalse();
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

        await Assert.That(indexerClass.GetElementType(Poly.Interpretation.CSharp.CSharpTypeSystem.Shared)).IsNull();
    }

    private static ITypeDefinition Resolve(TypeDefinitionNode node) {
        var context = AnalysisContext.CreateDefault();
        new TypeDefinitionNodeAnalyzer().Analyze(context, node);
        return context.TypeSystem.Resolve(node.Name)!;
    }

    private sealed class TestParameter(string name) : IParameter {
        public int Position => 0;
        public string Name => name;
        public ITypeDefinition ParameterTypeDefinition => ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(typeof(object));
        public bool IsOptional => false;
        public object? DefaultValue => null;
    }
}