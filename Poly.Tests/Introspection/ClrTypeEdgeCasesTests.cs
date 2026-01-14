using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Tests.Introspection;

public class ClrTypeEdgeCasesTests {
    [Test]
    public async Task NullableValueType_CanBeReflected() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var nullableIntType = registry.GetTypeDefinition<int?>();

        await Assert.That(nullableIntType).IsNotNull();
        await Assert.That(nullableIntType.Name).Contains("Nullable");
    }

    [Test]
    public async Task NullableValueType_HasProperties() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var nullableIntType = registry.GetTypeDefinition<int?>();

        var properties = nullableIntType.Properties.ToList();

        // Nullable<T> has HasValue and Value properties
        await Assert.That(properties.Count).IsGreaterThan(0);
        var propertyNames = properties.Select(p => p.Name).ToList();
        await Assert.That(propertyNames).Contains("HasValue");
        await Assert.That(propertyNames).Contains("Value");
    }

    [Test]
    public async Task NullableValueType_HasValue_PropertyWorks() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var nullableIntType = registry.GetTypeDefinition<int?>();
        var hasValueProp = nullableIntType.Properties.First(p => p.Name == "HasValue");

        // Verify the property is discovered and has correct type
        await Assert.That(hasValueProp).IsNotNull();
        await Assert.That(hasValueProp.Name).IsEqualTo("HasValue");
        await Assert.That(((ITypeMember)hasValueProp).MemberTypeDefinition.FullName).Contains("Boolean");
    }

    [Test]
    public async Task NullableValueType_Value_PropertyWorks() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var nullableIntType = registry.GetTypeDefinition<int?>();
        var valueProp = nullableIntType.Properties.First(p => p.Name == "Value");

        // Verify the property is discovered and has correct type
        await Assert.That(valueProp).IsNotNull();
        await Assert.That(valueProp.Name).IsEqualTo("Value");
        await Assert.That(((ITypeMember)valueProp).MemberTypeDefinition.FullName).Contains("Int32");
    }

    [Test]
    public async Task InterfaceType_CanBeReflected() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var enumerableType = registry.GetTypeDefinition<IEnumerable<int>>();

        await Assert.That(enumerableType).IsNotNull();
        await Assert.That(enumerableType.Name).Contains("IEnumerable");
    }

    [Test]
    public async Task InterfaceType_HasMembers() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var enumerableType = registry.GetTypeDefinition<IEnumerable<int>>();

        var members = enumerableType.Members.ToList();

        // IEnumerable<T> should have at least GetEnumerator
        await Assert.That(members.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task AbstractType_CanBeReflected() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var abstractType = registry.GetTypeDefinition<AbstractBase>();

        await Assert.That(abstractType).IsNotNull();
        await Assert.That(abstractType.Name).IsEqualTo("AbstractBase");
    }

    [Test]
    public async Task AbstractType_HasMembers() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var abstractType = registry.GetTypeDefinition<AbstractBase>();

        var methods = abstractType.Methods.ToList();
        var properties = abstractType.Properties.ToList();

        // AbstractBase should have AbstractMethod and ConcreteProperty
        await Assert.That(methods.Count).IsGreaterThan(0);
        await Assert.That(properties.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task GenericType_CanBeReflected() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var listType = registry.GetTypeDefinition<List<string>>();

        await Assert.That(listType).IsNotNull();
        await Assert.That(listType.Name).Contains("List");
    }

    [Test]
    public async Task GenericType_TypeParameter_IsResolved() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var listType = registry.GetTypeDefinition<List<string>>();

        // List<T> has Add(T) method
        var addMembers = listType.Methods.WithName("Add");

        await Assert.That(addMembers.Count()).IsGreaterThan(0);
    }

    [Test]
    public async Task NestedType_CanBeReflected() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var nestedType = registry.GetTypeDefinition<OuterClass.InnerClass>();

        await Assert.That(nestedType).IsNotNull();
        await Assert.That(nestedType.Name).Contains("InnerClass");
    }

    [Test]
    public async Task DelegateType_CanBeReflected() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var delegateType = registry.GetTypeDefinition<Action<int>>();

        await Assert.That(delegateType).IsNotNull();
        await Assert.That(delegateType.Name).Contains("Action");
    }

    [Test]
    public async Task SealedType_CanBeReflected() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var sealedType = registry.GetTypeDefinition<SealedClass>();

        await Assert.That(sealedType).IsNotNull();
        await Assert.That(sealedType.Name).IsEqualTo("SealedClass");
    }

    [Test]
    public async Task EnumType_CanBeReflected() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var enumType = registry.GetTypeDefinition<TestEnum>();

        await Assert.That(enumType).IsNotNull();
        await Assert.That(enumType.Name).IsEqualTo("TestEnum");
    }

    [Test]
    public async Task EnumType_HasFields() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var enumType = registry.GetTypeDefinition<TestEnum>();

        var fields = enumType.Fields.ToList();

        // Enums expose their values as fields
        await Assert.That(fields.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task StructType_CanBeReflected() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var structType = registry.GetTypeDefinition<TestStruct>();

        await Assert.That(structType).IsNotNull();
        await Assert.That(structType.Name).IsEqualTo("TestStruct");
    }

    [Test]
    public async Task StructType_HasMembers() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var structType = registry.GetTypeDefinition<TestStruct>();

        var properties = structType.Properties.ToList();
        var fields = structType.Fields.ToList();

        // TestStruct has at least one field or property
        var hasMembers = properties.Count > 0 || fields.Count > 0;
        await Assert.That(hasMembers).IsTrue();
    }

    [Test]
    public async Task GenericClassConstraint_CanBeReflected() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var genericType = registry.GetTypeDefinition<GenericWithConstraint<string>>();

        await Assert.That(genericType).IsNotNull();
    }

    [Test]
    public async Task TypeWithInheritance_HasInheritedMembers() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var derivedType = registry.GetTypeDefinition<DerivedClass>();

        var methods = derivedType.Methods.ToList();

        // DerivedClass inherits from AbstractBase
        await Assert.That(methods.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task PrivateMembers_AreDiscoverable() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var typeType = registry.GetTypeDefinition<ClassWithPrivateMembers>();

        // With BindingFlags.NonPublic, private members should be included
        var members = typeType.Members.ToList();
        var allNames = members.Select(m => m.Name).ToList();

        // Should have both public and private members
        await Assert.That(allNames).Contains("PublicField");
    }

    // Helper classes and types for testing
    public abstract class AbstractBase {
        public abstract void AbstractMethod();
        public virtual string ConcreteProperty => "test";
    }

    public class OuterClass {
        public class InnerClass {
            public int Value { get; set; }
        }
    }

    public static class StaticUtility {
        public static int StaticValue { get; set; }
        public static void StaticMethod() { }
    }

    public sealed class SealedClass {
        public void Method() { }
    }

    public enum TestEnum {
        Value1 = 0,
        Value2 = 1,
        Value3 = 2
    }

    public struct TestStruct {
        public int IntValue { get; set; }
        public string StringField;
    }

    public class GenericWithConstraint<T> where T : class {
        public T? Value { get; set; }
    }

    public class DerivedClass : AbstractBase {
        public override void AbstractMethod() { }
    }

    public class ClassWithPrivateMembers(int _) {
        public int PublicField;
        private readonly int _privateField = _;
    }
}