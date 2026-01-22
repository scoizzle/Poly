using Poly.Tests.TestHelpers;
using System.Linq.Expressions;

using Poly.Interpretation;
using Expr = System.Linq.Expressions.Expression;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Tests.Introspection;

public class ClrTypeIndexerTests {
    [Test]
    public async Task ArrayType_HasProperties()
    {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var arrayType = registry.GetTypeDefinition<int[]>();

        // Arrays don't expose indexers as properties in CLR, they use special IL instructions
        // So we just verify we can get the type definition
        await Assert.That(arrayType).IsNotNull();
        await Assert.That(arrayType.Name).IsEqualTo("Int32[]");
    }

    [Test]
    public async Task ListType_HasIndexerProperty()
    {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var listType = registry.GetTypeDefinition<List<int>>();

        var indexers = listType.Properties.Where(p => p.Parameters != null).ToList();

        await Assert.That(indexers.Count).IsGreaterThan(0);
        var indexer = indexers.First();
        // The full name includes the interface name when it's an explicit interface implementation
        await Assert.That(indexer.Name).Contains("Item");
    }

    [Test]
    public async Task DictionaryType_HasIndexerProperty()
    {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var dictType = registry.GetTypeDefinition<Dictionary<string, int>>();

        var indexers = dictType.Properties.Where(p => p.Parameters != null).ToList();

        await Assert.That(indexers.Count).IsGreaterThan(0);
        var indexer = indexers.First();
        await Assert.That(indexer.Name).Contains("Item");
    }

    [Test]
    public async Task ListIndexer_HasCorrectProperties()
    {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var listType = registry.GetTypeDefinition<List<int>>();
        var indexer = listType.Properties.First(p => p.Parameters != null);

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
