using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Tests.Introspection;

public class ClrTypePropertyTests {
    private sealed class InitOnlyPropertyHolder {
        public int Value { get; init; }
    }

    [Test]
    public async Task LengthProperty_HasCorrectProperties() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var stringType = registry.GetTypeDefinition<string>();
        var lengthProperty = stringType.Properties.WithName("Length").SingleOrDefault();

        await Assert.That(lengthProperty).IsNotNull();
        await Assert.That(lengthProperty).IsTypeOf<ClrTypeProperty>();
        await Assert.That(lengthProperty!.Name).IsEqualTo("Length");
        await Assert.That(((ITypeMember)lengthProperty).DeclaringTypeDefinition).IsEqualTo(stringType);
        await Assert.That(((ITypeMember)lengthProperty).MemberTypeDefinition.FullName).IsEqualTo("System.Int32");
    }

    [Test]
    public async Task Property_ToString_HasCorrectFormat() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var stringType = registry.GetTypeDefinition<string>();
        var lengthProperty = stringType.Properties.WithName("Length").SingleOrDefault() as ClrTypeProperty;

        await Assert.That(lengthProperty).IsNotNull();
        var toStringResult = lengthProperty!.ToString();
        await Assert.That(toStringResult).Contains("Int32");
        await Assert.That(toStringResult).Contains("String");
        await Assert.That(toStringResult).Contains("Length");
    }

    [Test]
    public async Task StaticProperty_HasCorrectProperties() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var dateTimeType = registry.GetTypeDefinition<DateTime>();
        var nowProperty = dateTimeType.Properties.WithName("Now").SingleOrDefault();

        await Assert.That(nowProperty).IsNotNull();
        await Assert.That(nowProperty).IsTypeOf<ClrTypeProperty>();
        await Assert.That(nowProperty!.Name).IsEqualTo("Now");
        await Assert.That(((ITypeMember)nowProperty).MemberTypeDefinition.FullName).IsEqualTo("System.DateTime");
    }

    [Test]
    public async Task LengthProperty_ExposesReadAccessorOnly() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var stringType = registry.GetTypeDefinition<string>();
        var lengthProperty = stringType.Properties.WithName("Length").SingleOrDefault() as ClrTypeProperty;

        await Assert.That(lengthProperty).IsNotNull();
        await Assert.That(lengthProperty!.CanRead).IsTrue();
        await Assert.That(lengthProperty.CanWrite).IsFalse();
        await Assert.That(lengthProperty.CanInitialize).IsFalse();
    }

    [Test]
    public async Task InitOnlyProperty_ExposesInitializeCapability() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var type = registry.GetTypeDefinition<InitOnlyPropertyHolder>();
        var valueProperty = type.Properties.WithName("Value").SingleOrDefault() as ClrTypeProperty;

        await Assert.That(valueProperty).IsNotNull();
        await Assert.That(valueProperty!.CanRead).IsTrue();
        await Assert.That(valueProperty.CanWrite).IsFalse();
        await Assert.That(valueProperty.CanInitialize).IsTrue();
    }
}