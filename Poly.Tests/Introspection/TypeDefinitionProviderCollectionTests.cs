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

    // Mock implementations for testing
    private class MockTypeDefinition : ITypeDefinition {
        public MockTypeDefinition(string name) {
            Name = name;
        }

        public string Name { get; }
        public string? Namespace => null;
        public IEnumerable<ITypeMember> Members => Array.Empty<ITypeMember>();
        public IEnumerable<IMethod> Methods => Array.Empty<IMethod>();
        public Type ReflectedType => typeof(object);
        public ITypeMember? GetMember(string name) => null;
    }

    private class MockTypeDefinitionProvider : ITypeDefinitionProvider {
        private readonly Dictionary<string, ITypeDefinition> _types = new();

        public void AddType(string name, ITypeDefinition type) {
            _types[name] = type;
        }

        public ITypeDefinition? GetTypeDefinition(string name) {
            return _types.TryGetValue(name, out var type) ? type : null;
        }
    }
}