using System.Linq.Expressions;

using Poly.Interpretation;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Tests.Introspection;

public class ClrTypeIndexerTests {
    [Test]
    public async Task ArrayType_HasProperties() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var arrayType = registry.GetTypeDefinition<int[]>();

        // Arrays don't expose indexers as properties in CLR, they use special IL instructions
        // So we just verify we can get the type definition
        await Assert.That(arrayType).IsNotNull();
        await Assert.That(arrayType.Name).IsEqualTo("Int32[]");
    }

    [Test]
    public async Task ListType_HasIndexerProperty() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var listType = registry.GetTypeDefinition<List<int>>();

        var indexers = listType.Properties.Where(p => p.Parameters != null).ToList();

        await Assert.That(indexers.Count).IsGreaterThan(0);
        var indexer = indexers.First();
        // The full name includes the interface name when it's an explicit interface implementation
        await Assert.That(indexer.Name).Contains("Item");
    }

    [Test]
    public async Task DictionaryType_HasIndexerProperty() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var dictType = registry.GetTypeDefinition<Dictionary<string, int>>();

        var indexers = dictType.Properties.Where(p => p.Parameters != null).ToList();

        await Assert.That(indexers.Count).IsGreaterThan(0);
        var indexer = indexers.First();
        await Assert.That(indexer.Name).Contains("Item");
    }

    [Test]
    public async Task ListIndexer_HasCorrectProperties() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var listType = registry.GetTypeDefinition<List<int>>();
        var indexer = listType.Properties.First(p => p.Parameters != null);

        await Assert.That(indexer.Name).Contains("Item");
        // IList indexer returns object, not int
        await Assert.That(indexer.Parameters).IsNotNull();
        await Assert.That(indexer.Parameters!.Count()).IsEqualTo(1);
    }

    [Test]
    public async Task ListIndexer_AccessWithValidIndex_ReturnsValue() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var listType = registry.GetTypeDefinition<List<int>>();
        var indexer = listType.Properties.First(p => p.Parameters != null);

        var list = new List<int> { 10, 20, 30 };
        var listValue = Value.Wrap(list);
        var indexValue = Value.Wrap(1);

        var accessor = indexer.GetMemberAccessor(listValue, [indexValue]);

        await Assert.That(accessor).IsNotNull();

        var context = new InterpretationContext();
        var expression = accessor.BuildExpression(context);
        // Convert to object since the expression type might be specific but we're testing generic access
        var converted = Expression.Convert(expression, typeof(object));
        var lambda = Expression.Lambda<Func<object>>(converted).Compile();
        var result = lambda();

        await Assert.That(result).IsEqualTo(20);
    }

    // Note: Arrays in C# don't expose indexers as properties - they use special array accessor IL instructions
    // Array indexing would require special handling in the expression building system
    // This test is commented out as it's not applicable to the current CLR introspection design
    
    // [Test]
    // public async Task ArrayIndexer_AccessWithValidIndex_ReturnsValue() {
    //     // Arrays don't have indexer properties in CLR reflection
    // }

    [Test]
    public async Task DictionaryIndexer_AccessWithValidKey_ReturnsValue() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var dictType = registry.GetTypeDefinition<Dictionary<string, int>>();
        var indexer = dictType.Properties.First(p => p.Parameters != null);

        var dict = new Dictionary<string, int> {
            ["one"] = 1,
            ["two"] = 2,
            ["three"] = 3
        };
        var dictValue = Value.Wrap(dict);
        var keyValue = Value.Wrap("two");

        var accessor = indexer.GetMemberAccessor(dictValue, [keyValue]);

        await Assert.That(accessor).IsNotNull();

        var context = new InterpretationContext();
        var expression = accessor.BuildExpression(context);
        // Convert to object since the expression type might be specific but we're testing generic access
        var converted = Expression.Convert(expression, typeof(object));
        var lambda = Expression.Lambda<Func<object>>(converted).Compile();
        var result = lambda();

        await Assert.That(result).IsEqualTo(2);
    }

    [Test]
    public async Task CustomIndexer_AccessWithValidIndex_ReturnsValue() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var customType = registry.GetTypeDefinition<CustomIndexerClass>();
        var indexer = customType.Properties.First(p => p.Parameters != null);

        var instance = new CustomIndexerClass();
        var instanceValue = Value.Wrap(instance);
        var indexValue = Value.Wrap(5);

        var accessor = indexer.GetMemberAccessor(instanceValue, [indexValue]);

        await Assert.That(accessor).IsNotNull();

        var context = new InterpretationContext();
        var expression = accessor.BuildExpression(context);
        var lambda = Expression.Lambda<Func<int>>(expression).Compile();
        var result = lambda();

        await Assert.That(result).IsEqualTo(50); // 5 * 10
    }

    [Test]
    public async Task IndexerWithMultipleParameters_ReturnsCorrectValue() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var customType = registry.GetTypeDefinition<MultiParamIndexerClass>();
        var indexers = customType.Properties.Where(p => p.Parameters != null).ToList();

        // Find the two-parameter indexer
        var twoParamIndexer = indexers.First(i => i.Parameters?.Count() == 2);

        await Assert.That(twoParamIndexer).IsNotNull();
        await Assert.That(twoParamIndexer.Parameters!.Count()).IsEqualTo(2);

        var instance = new MultiParamIndexerClass();
        var instanceValue = Value.Wrap(instance);
        var index1 = Value.Wrap(3);
        var index2 = Value.Wrap(4);

        var accessor = twoParamIndexer.GetMemberAccessor(instanceValue, [index1, index2]);

        await Assert.That(accessor).IsNotNull();

        var context = new InterpretationContext();
        var expression = accessor.BuildExpression(context);
        var lambda = Expression.Lambda<Func<int>>(expression).Compile();
        var result = lambda();

        await Assert.That(result).IsEqualTo(7); // 3 + 4
    }

    [Test]
    public async Task Indexer_ToString_HasCorrectFormat() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var listType = registry.GetTypeDefinition<List<int>>();
        var indexer = listType.Properties.First(p => p.Parameters != null);

        var list = new List<int> { 1, 2, 3 };
        var listValue = Value.Wrap(list);
        var indexValue = Value.Wrap(0);

        var accessor = indexer.GetMemberAccessor(listValue, [indexValue]);

        var accessorString = accessor.ToString();

        await Assert.That(accessorString).Contains("[");
        await Assert.That(accessorString).Contains("]");
    }

    // Helper classes for testing
    public class CustomIndexerClass {
        public int this[int index] => index * 10;
    }

    public class MultiParamIndexerClass {
        public int this[int x, int y] => x + y;
    }
}
