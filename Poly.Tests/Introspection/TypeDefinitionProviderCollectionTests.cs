using Poly.Introspection;

namespace Poly.Tests.Introspection;

public class TypeDefinitionProviderCollectionTests {
    [Test]
    public async Task GetTypeDefinition_ReturnsFromFirstProvider() {
        var mockProvider1 = new MockTypeDefinitionProvider();
        var mockProvider2 = new MockTypeDefinitionProvider();
        var collection = new TypeDefinitionProviderCollection();
        collection.AddProvider(mockProvider1);
        collection.AddProvider(mockProvider2);

        // Mock provider1 returns a type for "TestType"
        mockProvider1.AddType("TestType", new MockTypeDefinition("TestType"));
        var result = collection.GetTypeDefinition("TestType");

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Name).IsEqualTo("TestType");
    }

    [Test]
    public async Task GetTypeDefinition_ReturnsNullIfNoProviderHasType() {
        var mockProvider1 = new MockTypeDefinitionProvider();
        var mockProvider2 = new MockTypeDefinitionProvider();
        var collection = new TypeDefinitionProviderCollection();
        collection.AddProvider(mockProvider1);
        collection.AddProvider(mockProvider2);

        var result = collection.GetTypeDefinition("NonExistentType");

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task AddProvider_AddsProviderToCollection() {
        var collection = new TypeDefinitionProviderCollection();
        var provider = new MockTypeDefinitionProvider();

        collection.AddProvider(provider);

        provider.AddType("AddedType", new MockTypeDefinition("AddedType"));
        var result = collection.GetTypeDefinition("AddedType");
        await Assert.That(result).IsNotNull();
    }

    [Test]
    public async Task GetTypeDefinition_ReturnsFromFirstProviderWhenMultipleHaveSameType() {
        var mockProvider1 = new MockTypeDefinitionProvider();
        var mockProvider2 = new MockTypeDefinitionProvider();
        var collection = new TypeDefinitionProviderCollection();
        collection.AddProvider(mockProvider1);
        collection.AddProvider(mockProvider2);

        // Both providers have "SharedType", but with different instances
        var type1 = new MockTypeDefinition("SharedType") { Tag = "Provider1" };
        var type2 = new MockTypeDefinition("SharedType") { Tag = "Provider2" };
        mockProvider1.AddType("SharedType", type1);
        mockProvider2.AddType("SharedType", type2);

        var result = collection.GetTypeDefinition("SharedType");

        await Assert.That(result).IsNotNull();
        // The collection uses LIFO (stack) order, so the last added provider (mockProvider2) is checked first
        await Assert.That(((MockTypeDefinition)result!).Tag).IsEqualTo("Provider2");
    }

    // Mock implementations for testing
    private class MockTypeDefinition(string name) : ITypeDefinition {
        public string Name { get; } = name;
        public string? Namespace => null;
        public IEnumerable<ITypeMember> Members => [];
        public IEnumerable<IMethod> Methods => [];
        public Type ReflectedType => typeof(object);
        public IEnumerable<ITypeMember> GetMembers(string name) => Enumerable.Empty<ITypeMember>();
        public string? Tag { get; set; }
    }

    private class MockTypeDefinitionProvider : ITypeDefinitionProvider {
        private readonly Dictionary<string, ITypeDefinition> _types = [];

        public void AddType(string name, ITypeDefinition type) {
            _types[name] = type;
        }

        public ITypeDefinition? GetTypeDefinition(string name) {
            return _types.TryGetValue(name, out var type) ? type : null;
        }

        public ITypeDefinition? GetTypeDefinition(Type type) {
            throw new NotImplementedException();
        }
    }
}