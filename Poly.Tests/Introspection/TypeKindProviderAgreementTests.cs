using Poly.Analysis;
using Poly.Ast.Nodes;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.CSharp;
using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Tests.Introspection;

/// <summary>
/// Proves the two providers agree on type kind, that a closed <see cref="TypeKind"/>
/// cannot express an illegal combination, and that no producer or printer invents a
/// kind. A core attribute that depends on which provider answered is the defect this
/// file guards.
/// </summary>
public class TypeKindProviderAgreementTests {
    [Test]
    public async Task PrimitiveType_Boolean_ReportsBooleanShape() {
        ITypeDefinition boolean = ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(typeof(bool));

        await Assert.That(boolean.PrimitiveType).IsEqualTo(PrimitiveType.Boolean);
        await Assert.That(boolean.Kind).IsEqualTo(TypeKind.Primitive);
    }

    [Test]
    public async Task ClrProvider_Object_IsTheStructurePrimitive() {
        await Assert.That(typeof(object).GetPrimitiveType()).IsEqualTo(PrimitiveType.Structure);
    }

    [Test]
    public async Task ClrProvider_EveryPrimitive_ReportsPrimitiveKindAndItself() {
        foreach (var primitive in Enum.GetValues<PrimitiveType>()) {
            var clrType = primitive.GetClrType();
            await Assert.That(clrType).IsNotNull().Because($"'{primitive}' has no CLR mapping");
            ITypeDefinition definition = ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(clrType!);

            await Assert.That(definition.Kind).IsEqualTo(TypeKind.Primitive)
                .Because($"'{primitive}' must report Primitive");
            await Assert.That(definition.PrimitiveType).IsEqualTo(primitive)
                .Because($"'{primitive}' must report its own id");
        }
    }

    [Test]
    public async Task ClrProvider_PrimitiveTypeNonNull_Iff_PrimitiveKind() {
        Type[] samples = [
            typeof(int), typeof(string), typeof(object), typeof(DateTime),
            typeof(Uri), typeof(KeyValuePair<int, int>), typeof(IDisposable), typeof(DayOfWeek), typeof(Action),
        ];

        foreach (var sample in samples) {
            ITypeDefinition definition = ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(sample);
            await Assert.That(definition.PrimitiveType is not null)
                .IsEqualTo(definition.Kind == TypeKind.Primitive)
                .Because($"'{sample}' must agree on primitive-ness");
        }
    }

    [Test]
    public async Task AstProvider_PrimitiveTypeNonNull_Iff_PrimitiveKind() {
        TypeDefinitionNode[] nodes = [
            new("Plain", Namespace: "Sample"),
            new("Status", Kind: TypeKind.Enum),
            new("IWidget", Kind: TypeKind.Interface),
            new("Atomic", PrimitiveTypeId: PrimitiveType.Int32),
        ];

        foreach (var node in nodes) {
            var definition = Resolve(node);
            await Assert.That(definition.PrimitiveType is not null)
                .IsEqualTo(definition.Kind == TypeKind.Primitive)
                .Because($"'{node.Name}' must agree on primitive-ness");
        }
    }

    [Test]
    public async Task TypeKind_DefaultIsNotPrimitive() {
        // A zero-initialized field must not claim the one kind that requires a
        // PrimitiveType. Class is the safe default.
        TypeKind uninitialized = new();
        await Assert.That(uninitialized is TypeKind.Primitive).IsFalse();
    }

    [Test]
    public async Task AstProvider_PrimitiveKindWithoutPrimitiveId_Throws() {
        var node = new TypeDefinitionNode("Broken", Kind: TypeKind.Primitive);

        await Assert.That(() => Resolve(node).Kind).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task TypeKind_IsClosedSet_NoIllegalCombination() {
        var kinds = Enum.GetValues<TypeKind>();

        await Assert.That(kinds.Length).IsEqualTo(8);
        await Assert.That(typeof(TypeKind).IsDefined(typeof(FlagsAttribute), inherit: false)).IsFalse();

        // One distinct shape descriptor per value: every kind is its own shape, so
        // "integer and floating point at once" has nowhere to live.
        var shapes = kinds.Select(kind => kind switch {
            TypeKind.Primitive => "atomic",
            TypeKind.Class => "reference",
            TypeKind.Struct => "value",
            TypeKind.Interface => "interface",
            TypeKind.Enum => "enum",
            TypeKind.Delegate => "delegate",
            TypeKind.Union => "union",
            TypeKind.Tuple => "product",
            _ => throw new InvalidOperationException($"Unmapped kind '{kind}'."),
        }).ToArray();

        await Assert.That(shapes.Distinct(StringComparer.Ordinal).Count()).IsEqualTo(kinds.Length);
    }

    [Test]
    public async Task AstProvider_ProducedKinds_AreReachable() {
        await Assert.That(Resolve(new TypeDefinitionNode("Plain", Namespace: "Sample")).Kind)
            .IsEqualTo(TypeKind.Class);
        await Assert.That(Resolve(new TypeDefinitionNode("Status", Kind: TypeKind.Enum)).Kind)
            .IsEqualTo(TypeKind.Enum);
        await Assert.That(Resolve(new TypeDefinitionNode("IWidget", Kind: TypeKind.Interface)).Kind)
            .IsEqualTo(TypeKind.Interface);
        await Assert.That(Resolve(new TypeDefinitionNode("Atomic", PrimitiveTypeId: PrimitiveType.Int32)).Kind)
            .IsEqualTo(TypeKind.Primitive);
    }

    [Test]
    public async Task ClrProvider_StructAndDelegate_AreReachable() {
        var registry = ClrTypeDefinitionRegistry.Shared;

        // Object, string, and DateTime are primitives, never the CLR shape they look like.
        await Assert.That(registry.GetTypeDefinition(typeof(Uri)).Kind).IsEqualTo(TypeKind.Class);
        await Assert.That(registry.GetTypeDefinition(typeof(KeyValuePair<int, int>)).Kind).IsEqualTo(TypeKind.Struct);
        await Assert.That(registry.GetTypeDefinition(typeof(Action)).Kind).IsEqualTo(TypeKind.Delegate);
        await Assert.That(registry.GetTypeDefinition(typeof(object)).Kind).IsEqualTo(TypeKind.Primitive);
        await Assert.That(registry.GetTypeDefinition(typeof(string)).Kind).IsEqualTo(TypeKind.Primitive);
        await Assert.That(registry.GetTypeDefinition(typeof(DateTime)).Kind).IsEqualTo(TypeKind.Primitive);
    }

    [Test]
    public async Task SyntheticAstTypes_ReportClass() {
        ITypeDefinition collection = new AstCollectionTypeDefinition(
            ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(typeof(int)), CollectionKind.List);
        ITypeDefinition parameter = new AstGenericParameterTypeDefinition(
            new TestParameter("T"), collection);

        await Assert.That(collection.Kind).IsEqualTo(TypeKind.Class);
        await Assert.That(collection.PrimitiveType).IsNull();
        await Assert.That(parameter.Kind).IsEqualTo(TypeKind.Class);
        await Assert.That(parameter.PrimitiveType).IsNull();
    }

    [Test]
    public async Task Writer_StructKind_Throws() {
        var node = new TypeDefinitionNode("Broken", Kind: TypeKind.Struct);

        await Assert.That(() => new CSharpGenerator().Generate(node))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Writer_NonPrintableKinds_Throw() {
        // Union and Tuple are reserved: no producer exists, and the writer must fail
        // closed rather than print them as a class. Delegate has a CLR producer but no
        // reader or printer arm.
        await Assert.That(() => new CSharpGenerator().Generate(new TypeDefinitionNode("Either", Kind: TypeKind.Union)))
            .Throws<InvalidOperationException>();
        await Assert.That(() => new CSharpGenerator().Generate(new TypeDefinitionNode("Pair", Kind: TypeKind.Tuple)))
            .Throws<InvalidOperationException>();
        await Assert.That(() => new CSharpGenerator().Generate(new TypeDefinitionNode("Callback", Kind: TypeKind.Delegate)))
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