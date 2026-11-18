using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Tests.Introspection;

public class TypeDefinitionExtensionsTests {
    [Test]
    public async Task IsSameType_ThirdPlatformDefinitions_CompareByTheirOwnAnswer() {
        // I1: identity is the platform's. These definitions are neither CLR nor AST, so a root that decides identity
        // with its own type tests would answer "not the same" even when the platform says they are.
        ITypeDefinition first = new NamedDefinition("Money", "Billing");
        ITypeDefinition second = new NamedDefinition("Money", "Billing");

        await Assert.That(TypeDefinitionExtensions.IsSameType(first, second)).IsTrue();
    }

    /// <summary>A definition from a platform the root knows nothing about: it answers its own identity.</summary>
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
        public PrimitiveType? PrimitiveType => null;
        public ITypeDefinition? NullableOf => null;

        public bool IsSameTypeAs(ITypeDefinition other) =>
            other is NamedDefinition candidate
            && string.Equals(candidate.Name, Name, StringComparison.Ordinal)
            && string.Equals(candidate.Namespace, ns, StringComparison.Ordinal);

        IEnumerable<ITypeConversion> ITypeDefinition.Conversions => [];
        bool ITypeDefinition.IsCollectionShape => false;
        ITypeDefinition? ITypeDefinition.SequenceElementType => null;
        ITypeOperator? ITypeDefinition.ResolveOperator(OperatorKind kind, ITypeDefinition? right) => null;
        ITypeConversion? ITypeDefinition.ResolveConversionFrom(ITypeDefinition source) => null;
    }


    [Test]
    public async Task GetElementType_ArrayType_ReturnsArrayElementType() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var arrayType = registry.GetTypeDefinition<int[]>();

        var elementType = arrayType.GetElementType();

        await Assert.That(elementType).IsNotNull();
        await Assert.That(elementType!.GetRuntimeType()).IsEqualTo(typeof(int));
    }

    [Test]
    public async Task GetElementType_ListType_ReturnsIndexerElementType() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var listType = registry.GetTypeDefinition<List<int>>();

        var elementType = listType.GetElementType();

        await Assert.That(elementType).IsNotNull();
        await Assert.That(elementType!.GetRuntimeType()).IsEqualTo(typeof(int));
    }

    [Test]
    public async Task GetElementType_DictionaryWithStringKey_ReturnsValueTypeWhenKeyTypeProvided() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var dictionaryType = registry.GetTypeDefinition<Dictionary<string, int>>();
        var stringType = registry.GetTypeDefinition<string>();
        ITypeDefinition[] keyTypes = [stringType];

        var elementType = dictionaryType.GetElementType(keyTypes);

        await Assert.That(elementType).IsNotNull();
        await Assert.That(elementType!.GetRuntimeType()).IsEqualTo(typeof(int));
    }

    [Test]
    public async Task GetElementType_MultiParameterIndexer_ReturnsIndexedValueTypeWhenParameterTypesMatch() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var indexerType = registry.GetTypeDefinition<ClrTypeIndexerTests.MultiParamIndexerClass>();
        var intType = registry.GetTypeDefinition<int>();
        ITypeDefinition[] indexTypes = [intType, intType];

        var elementType = indexerType.GetElementType(indexTypes);

        await Assert.That(elementType).IsNotNull();
        await Assert.That(elementType!.GetRuntimeType()).IsEqualTo(typeof(int));
    }

    [Test]
    public async Task GetElementType_TypeWithoutIndexerOrSequence_ReturnsNull() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var objectType = registry.GetTypeDefinition<object>();

        var elementType = objectType.GetElementType();

        await Assert.That(elementType).IsNull();
    }
}