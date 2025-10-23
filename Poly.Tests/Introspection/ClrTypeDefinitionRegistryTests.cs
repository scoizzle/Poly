using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Tests.Introspection;

public class ClrTypeDefinitionRegistryTests {
    [Test]
    public async Task GetTypeDefinition_ReturnsCorrectType() {
        var registry = new ClrTypeDefinitionRegistry();

        var intType = registry.GetTypeDefinition<int>();

        await Assert.That(intType.FullName).IsEqualTo("System.Int32");
    }

    [Test]
    public async Task GetTypeDefinition_ByName_ReturnsCorrectType() {
        var registry = new ClrTypeDefinitionRegistry();

        var intType = registry.GetTypeDefinition("System.Int32");

        await Assert.That(intType).IsNotNull();
        await Assert.That(intType!.Name).IsEqualTo("Int32");
    }

    [Test]
    public async Task GetDeferredTypeDefinitionResolver_ReturnsLazy() {
        var registry = new ClrTypeDefinitionRegistry();

        var lazyInt = registry.GetDeferredTypeDefinitionResolver(typeof(int));

        await Assert.That(lazyInt).IsNotNull();
        var intType = lazyInt.Value;
        await Assert.That(intType.FullName).IsEqualTo("System.Int32");
    }

    [Test]
    public async Task SharedRegistry_IsSingleton() {
        var registry1 = ClrTypeDefinitionRegistry.Shared;
        var registry2 = ClrTypeDefinitionRegistry.Shared;

        await Assert.That(registry1).IsEqualTo(registry2);
    }

    [Test]
    public async Task AddType_AddsTypeToRegistry() {
        var registry = new ClrTypeDefinitionRegistry();
        var stringType = registry.GetTypeDefinition<string>();

        // Remove and re-add
        registry.RemoveType(stringType);
        registry.AddType(stringType);

        var retrievedType = registry.GetTypeDefinition("System.String");
        await Assert.That(retrievedType).IsNotNull();
        await Assert.That(retrievedType).IsEqualTo(stringType);
    }

    [Test]
    public async Task AddType_ThrowsOnDuplicate() {
        var registry = new ClrTypeDefinitionRegistry();
        var stringType = registry.GetTypeDefinition<string>();

        await Assert.That(() => registry.AddType(stringType)).Throws<ArgumentException>();
    }

    [Test]
    public async Task RemoveType_RemovesTypeFromRegistry() {
        var registry = new ClrTypeDefinitionRegistry();
        var stringType = registry.GetTypeDefinition<string>();

        registry.RemoveType(stringType);

        // After removal, getting by string name should recreate it
        var retrievedType = registry.GetTypeDefinition("System.String");
        await Assert.That(retrievedType).IsNotNull();
        // But it should be a different instance
        await Assert.That(retrievedType).IsNotEqualTo(stringType);
    }

    [Test]
    public async Task RemoveType_ThrowsOnNonExistent() {
        var registry = new ClrTypeDefinitionRegistry();
        var stringType = registry.GetTypeDefinition<string>();

        registry.RemoveType(stringType);

        await Assert.That(() => registry.RemoveType(stringType)).Throws<ArgumentException>();
    }

    [Test]
    public async Task GetTypeDefinition_ByName_ThrowsOnInvalidType() {
        var registry = new ClrTypeDefinitionRegistry();

        await Assert.That(() => registry.GetTypeDefinition("NonExistent.InvalidType"))
            .Throws<ArgumentException>();
    }
}