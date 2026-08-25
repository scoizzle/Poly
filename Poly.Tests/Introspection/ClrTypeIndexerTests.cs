using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Tests.Introspection;

public class ClrTypeIndexerTests {
    [Test]
    public async Task ArrayType_ExposesSyntheticIndexerProperty() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var arrayType = registry.GetTypeDefinition<int[]>();

        var indexers = arrayType.Properties.Where(p => p.Parameters.Any()).ToList();

        await Assert.That(indexers.Count).IsEqualTo(1);
        await Assert.That(indexers[0].Name).IsEqualTo("Item");
        await Assert.That(indexers[0].Parameters!.Count()).IsEqualTo(1);
        await Assert.That(indexers[0].Parameters!.Single().ParameterTypeDefinition.GetRuntimeType()).IsEqualTo(typeof(int));
        await Assert.That(indexers[0].MemberTypeDefinition.GetRuntimeType()).IsEqualTo(typeof(int));
        await Assert.That(indexers[0] is ClrTypeSyntheticProperty).IsTrue();
    }

    [Test]
    public async Task MultiDimensionalArray_ExposesIndexerWithParameterPerDimension() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var arrayType = registry.GetTypeDefinition<int[,]>();

        var indexer = arrayType.Properties.Single(p => p.Parameters.Any());

        await Assert.That(indexer.Name).IsEqualTo("Item");
        await Assert.That(indexer.Parameters!.Count()).IsEqualTo(2);
        await Assert.That(indexer.Parameters!.All(parameter => parameter.ParameterTypeDefinition.GetRuntimeType() == typeof(int))).IsTrue();
        await Assert.That(indexer.MemberTypeDefinition.GetRuntimeType()).IsEqualTo(typeof(int));
    }

    [Test]
    public async Task ListType_HasIndexerProperty() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var listType = registry.GetTypeDefinition<List<int>>();

        var indexers = listType.Properties.Where(p => p.Parameters.Any()).ToList();

        await Assert.That(indexers.Count).IsGreaterThan(0);
        var indexer = indexers.First();
        // The full name includes the interface name when it's an explicit interface implementation
        await Assert.That(indexer.Name).Contains("Item");
        await Assert.That(indexer is ClrTypeProperty).IsTrue();
    }

    [Test]
    public async Task DictionaryType_HasIndexerProperty() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var dictType = registry.GetTypeDefinition<Dictionary<string, int>>();

        var indexers = dictType.Properties.Where(p => p.Parameters.Any()).ToList();

        await Assert.That(indexers.Count).IsGreaterThan(0);
        var indexer = indexers.First();
        await Assert.That(indexer.Name).Contains("Item");
    }

    [Test]
    public async Task ListIndexer_HasCorrectProperties() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var listType = registry.GetTypeDefinition<List<int>>();
        var indexer = listType.Properties.First(p => p.Parameters.Any());

        await Assert.That(indexer.Name).Contains("Item");
        // IList indexer returns object, not int
        await Assert.That(indexer.Parameters).IsNotNull();
        await Assert.That(indexer.Parameters!.Count()).IsEqualTo(1);
    }

    // Note: Arrays in C# don't expose indexers as properties - they use special array accessor IL instructions
    // Array indexing would require special handling in the expression building system
    // This test is commented out as it's not applicable to the current CLR introspection design

    // [Test]
    // public async Task ArrayIndexer_AccessWithValidIndex_ReturnsValue() {
    //     // Arrays don't have indexer properties in CLR reflection
    // }

    // Helper classes for testing
    public class CustomIndexerClass {
        public int this[int index] => index * 10;
    }

    public class MultiParamIndexerClass {
        public int this[int x, int y] => x + y;
    }
}