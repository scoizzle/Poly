using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Tests.Introspection;

public class TypeDefinitionExtensionsTests {
    [Test]
    public async Task IsSameType_ThirdPlatformDefinitions_CompareByTheirOwnAnswer() {
        // I1: identity is the platform's: these definitions are neither CLR nor AST, and the system that made them says they
        // are the same (#998 T9: the system, not the definition, answers).
        ITypeDefinition first = new NamedDefinition("Money", "Billing");
        ITypeDefinition second = new NamedDefinition("Money", "Billing");

        await Assert.That(TypeDefinitionExtensions.IsSameType(first, second)).IsTrue();
    }

    /// <summary>A platform the root knows nothing about: its system decides identity by qualified name (#998 T9).</summary>
    private sealed class NamedPlatform : ITypeSystem {
        public static readonly NamedPlatform Instance = new();
        public ITypeSystem? Target => null;
        public IReadOnlyList<ITypeSource> Sources => [];
        public ITypeDefinition? Resolve(string qualifiedName) => null;
        public ITypeDefinition? Bind(string word) => null;
        public string? Spell(ITypeDefinition definition) => null;
        public bool IsSame(ITypeDefinition a, ITypeDefinition b) =>
            string.Equals(a.Name, b.Name, StringComparison.Ordinal) && string.Equals(a.Namespace, b.Namespace, StringComparison.Ordinal);
        public ITypeDefinition? NullableOf(ITypeDefinition definition) => null;
        public ITypeDefinition? ElementType(ITypeDefinition definition) => null;
        public bool IsAssignableWithoutConversion(ITypeDefinition destination, ITypeDefinition source) => IsSame(destination, source);
        public ITypeConversion? Conversion(ITypeDefinition source, ITypeDefinition destination) => null;
        public ITypeOperator? Operator(OperatorKind kind, ITypeDefinition left, ITypeDefinition? right) => null;
        public TRule? Rule<TRule>() where TRule : class, ITypeSystemRule => null;
    }

    /// <summary>A definition from a platform the root knows nothing about: its own system answers its identity.</summary>
    private sealed class NamedDefinition(string name, string? ns) : ITypeDefinition {
        public string Name => name;
        public string? Namespace => ns;
        public AccessModifier AccessModifier => AccessModifier.Public;
        public ITypeDefinition? BaseType => null;
        public IEnumerable<ITypeDefinition> Interfaces => [];
        public IEnumerable<IParameter> GenericParameters => [];
        public IEnumerable<ITypeMember> Members => [];
        public IEnumerable<ITypeField> Fields => [];
        public IEnumerable<ITypeProperty> Properties => [];
        public IEnumerable<ITypeMethod> Methods => [];
        public IEnumerable<ITypeConstructor> Constructors => [];
        public IEnumerable<ITypeOperator> Operators => [];
        public ITypeDefinition? NullableOf => null;

        public ITypeSystem System => NamedPlatform.Instance;

        IEnumerable<ITypeConversion> ITypeDefinition.Conversions => [];
        bool ITypeDefinition.IsCollectionShape => false;
        ITypeDefinition? ITypeDefinition.SequenceElementType => null;
    }


    [Test]
    public async Task GetElementType_ArrayType_ReturnsArrayElementType() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var arrayType = registry.GetTypeDefinition<int[]>();

        var elementType = arrayType.GetElementType(Poly.Interpretation.CSharp.CSharpTypeSystem.Shared);

        await Assert.That(elementType).IsNotNull();
        await Assert.That(elementType!.GetRuntimeType()).IsEqualTo(typeof(int));
    }

    [Test]
    public async Task GetElementType_ListType_ReturnsIndexerElementType() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var listType = registry.GetTypeDefinition<List<int>>();

        var elementType = listType.GetElementType(Poly.Interpretation.CSharp.CSharpTypeSystem.Shared);

        await Assert.That(elementType).IsNotNull();
        await Assert.That(elementType!.GetRuntimeType()).IsEqualTo(typeof(int));
    }

    [Test]
    public async Task GetElementType_DictionaryWithStringKey_ReturnsValueTypeWhenKeyTypeProvided() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var dictionaryType = registry.GetTypeDefinition<Dictionary<string, int>>();
        var stringType = registry.GetTypeDefinition<string>();
        ITypeDefinition[] keyTypes = [stringType];

        var elementType = dictionaryType.GetElementType(Poly.Interpretation.CSharp.CSharpTypeSystem.Shared, keyTypes);

        await Assert.That(elementType).IsNotNull();
        await Assert.That(elementType!.GetRuntimeType()).IsEqualTo(typeof(int));
    }

    [Test]
    public async Task GetElementType_MultiParameterIndexer_ReturnsIndexedValueTypeWhenParameterTypesMatch() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var indexerType = registry.GetTypeDefinition<ClrTypeIndexerTests.MultiParamIndexerClass>();
        var intType = registry.GetTypeDefinition<int>();
        ITypeDefinition[] indexTypes = [intType, intType];

        var elementType = indexerType.GetElementType(Poly.Interpretation.CSharp.CSharpTypeSystem.Shared, indexTypes);

        await Assert.That(elementType).IsNotNull();
        await Assert.That(elementType!.GetRuntimeType()).IsEqualTo(typeof(int));
    }

    [Test]
    public async Task GetElementType_TypeWithoutIndexerOrSequence_ReturnsNull() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var objectType = registry.GetTypeDefinition<object>();

        var elementType = objectType.GetElementType(Poly.Interpretation.CSharp.CSharpTypeSystem.Shared);

        await Assert.That(elementType).IsNull();
    }
}