using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Tests.Introspection;

public class ClrTypeDefinitionRegistryTests
{
    [Test]
    public async Task GetTypeDefinition_ReturnsCorrectType()
    {
        var registry = new ClrTypeDefinitionRegistry();

        var intType = registry.GetTypeDefinition<int>();

        await Assert.That(intType.FullName).IsEqualTo("System.Int32");
    }

    [Test]
    public async Task GetTypeDefinition_ByName_ReturnsCorrectType()
    {
        var registry = new ClrTypeDefinitionRegistry();

        var intType = registry.GetTypeDefinition("System.Int32");

        await Assert.That(intType).IsNotNull();
        await Assert.That(intType!.Name).IsEqualTo("Int32");
    }

    [Test]
    public async Task GetDeferredTypeDefinitionResolver_ReturnsLazy()
    {
        var registry = new ClrTypeDefinitionRegistry();

        var lazyInt = registry.GetDeferredTypeDefinitionResolver(typeof(int));

        await Assert.That(lazyInt).IsNotNull();
        var intType = lazyInt.Value;
        await Assert.That(intType.FullName).IsEqualTo("System.Int32");
    }

    [Test]
    public async Task SharedRegistry_IsSingleton()
    {
        var registry1 = ClrTypeDefinitionRegistry.Shared;
        var registry2 = ClrTypeDefinitionRegistry.Shared;

        await Assert.That(registry1).IsEqualTo(registry2);
    }
}