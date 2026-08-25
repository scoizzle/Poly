using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Tests.Introspection;

public class TypeDefinitionExtensionsTests {
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