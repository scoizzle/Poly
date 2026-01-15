using System.Linq.Expressions;

using Poly.Interpretation;
using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Tests.Introspection;

public class ClrTypeFieldTests {
    // Helper class with public fields for testing
    public class TestClass {
        public int PublicField = 42;
        public static readonly string StaticField = "static value";
        public string InstanceField = "instance value";
    }

    [Test]
    public async Task PublicField_HasCorrectProperties()
    {
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
    public async Task Field_GetMemberAccessor_ReturnsCorrectValue()
    {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var testType = registry.GetTypeDefinition<TestClass>();
        var publicField = testType.Fields.WithName("PublicField").SingleOrDefault();

        var testInstance = new TestClass { PublicField = 99 };
        var instanceValue = Value.Wrap(testInstance);
        var accessor = publicField!.GetMemberAccessor(instanceValue);

        await Assert.That(accessor).IsNotNull();

        var interpretationContext = new InterpretationContext();
        var expression = accessor.BuildExpression(interpretationContext);
        var lambda = Expression.Lambda<Func<int>>(expression).Compile();
        var result = lambda();

        await Assert.That(result).IsEqualTo(99);
    }

    [Test]
    public async Task Field_ToString_HasCorrectFormat()
    {
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
    public async Task StaticField_HasCorrectProperties()
    {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var testType = registry.GetTypeDefinition<TestClass>();
        var staticField = testType.Fields.WithName("StaticField").SingleOrDefault();

        await Assert.That(staticField).IsNotNull();
        await Assert.That(staticField).IsTypeOf<ClrTypeField>();
        await Assert.That(staticField!.Name).IsEqualTo("StaticField");
        await Assert.That(((ITypeMember)staticField).MemberTypeDefinition.FullName).IsEqualTo("System.String");
    }

    [Test]
    public async Task StaticField_GetMemberAccessor_ReturnsCorrectValue()
    {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var testType = registry.GetTypeDefinition<TestClass>();
        var staticField = testType.Fields.WithName("StaticField").SingleOrDefault();

        var accessor = staticField!.GetMemberAccessor(Value.Null);

        await Assert.That(accessor).IsNotNull();

        var interpretationContext = new InterpretationContext();
        var expression = accessor.BuildExpression(interpretationContext);
        var lambda = Expression.Lambda<Func<string>>(expression).Compile();
        var result = lambda();

        await Assert.That(result).IsEqualTo("static value");
    }

    [Test]
    public async Task FieldInfo_PropertyIsAccessible()
    {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var testType = registry.GetTypeDefinition<TestClass>();
        var publicField = testType.Fields.WithName("PublicField").SingleOrDefault() as ClrTypeField;

        await Assert.That(publicField).IsNotNull();
        await Assert.That(publicField!.FieldInfo).IsNotNull();
        await Assert.That(publicField.FieldInfo.Name).IsEqualTo("PublicField");
    }
}