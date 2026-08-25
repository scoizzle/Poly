using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Tests.Introspection;

public class ClrTypeFieldTests {
    // Helper class with public fields for testing
    public class TestClass {
        public int PublicField = 42;
        public static readonly string StaticField = "static value";
        public string InstanceField = "instance value";
        public const int ConstantField = 7;
    }

    [Test]
    public async Task PublicField_HasCorrectProperties() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var testType = registry.GetTypeDefinition<TestClass>();
        var publicField = testType.Fields.WithName("PublicField").SingleOrDefault();

        await Assert.That(publicField).IsNotNull();
        await Assert.That(publicField).IsTypeOf<ClrTypeField>();
        await Assert.That(publicField!.Name).IsEqualTo("PublicField");
        await Assert.That(((ITypeMember)publicField).DeclaringTypeDefinition).IsEqualTo(testType);
        await Assert.That(((ITypeMember)publicField).MemberTypeDefinition.FullName).IsEqualTo("System.Int32");
    }

    [Test]
    public async Task Field_ToString_HasCorrectFormat() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var testType = registry.GetTypeDefinition<TestClass>();
        var publicField = testType.Fields.WithName("PublicField").SingleOrDefault() as ClrTypeField;

        await Assert.That(publicField).IsNotNull();
        var toStringResult = publicField!.ToString();
        await Assert.That(toStringResult).Contains("Int32");
        await Assert.That(toStringResult).Contains("TestClass");
        await Assert.That(toStringResult).Contains("PublicField");
    }

    [Test]
    public async Task StaticField_HasCorrectProperties() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var testType = registry.GetTypeDefinition<TestClass>();
        var staticField = testType.Fields.WithName("StaticField").SingleOrDefault();

        await Assert.That(staticField).IsNotNull();
        await Assert.That(staticField).IsTypeOf<ClrTypeField>();
        await Assert.That(staticField!.Name).IsEqualTo("StaticField");
        await Assert.That(((ITypeMember)staticField).MemberTypeDefinition.FullName).IsEqualTo("System.String");
    }

    [Test]
    public async Task FieldInfo_PropertyIsAccessible() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var testType = registry.GetTypeDefinition<TestClass>();
        var publicField = testType.Fields.WithName("PublicField").SingleOrDefault() as ClrTypeField;

        await Assert.That(publicField).IsNotNull();
        await Assert.That(publicField!.FieldInfo).IsNotNull();
        await Assert.That(publicField.FieldInfo.Name).IsEqualTo("PublicField");
    }

    [Test]
    public async Task PublicField_ExposesReadAndWriteAccessors() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var testType = registry.GetTypeDefinition<TestClass>();
        var publicField = testType.Fields.WithName("PublicField").SingleOrDefault() as ClrTypeField;

        await Assert.That(publicField).IsNotNull();
        await Assert.That(publicField!.CanRead).IsTrue();
        await Assert.That(publicField.CanWrite).IsTrue();
        await Assert.That(publicField.CanInitialize).IsFalse();
    }

    [Test]
    public async Task ReadOnlyField_ExposesInitializeCapability() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var testType = registry.GetTypeDefinition<TestClass>();
        var staticField = testType.Fields.WithName("StaticField").SingleOrDefault() as ClrTypeField;

        await Assert.That(staticField).IsNotNull();
        await Assert.That(staticField!.CanRead).IsTrue();
        await Assert.That(staticField.CanWrite).IsFalse();
        await Assert.That(staticField.CanInitialize).IsTrue();
    }

    [Test]
    public async Task ConstantField_ExposesInitializeAccessor() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var testType = registry.GetTypeDefinition<TestClass>();
        var constantField = testType.Fields.WithName("ConstantField").SingleOrDefault() as ClrTypeField;

        await Assert.That(constantField).IsNotNull();
        await Assert.That(constantField!.CanRead).IsTrue();
        await Assert.That(constantField.CanWrite).IsFalse();
        await Assert.That(constantField.CanInitialize).IsTrue();
    }
}