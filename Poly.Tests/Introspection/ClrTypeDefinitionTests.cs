using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Tests.Introspection;

public class ClrTypeDefinitionTests {
    [Test]
    public async Task Int32Type_HasCorrectProperties() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var intType = registry.GetTypeDefinition<int>();

    await Assert.That(intType.Name).IsEqualTo("Int32");
    await Assert.That(intType.Namespace).IsEqualTo("System");
    await Assert.That(intType.FullName).IsEqualTo("System.Int32");
    await Assert.That(((ITypeDefinition)intType).ReflectedType).IsEqualTo(typeof(int));
    }

    [Test]
    public async Task Int32Type_HasMembers() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var intType = registry.GetTypeDefinition<int>();

        var members = intType.Members.ToList();

        await Assert.That(members.Count != 0).IsTrue();
        // Int32 has MaxValue, MinValue, etc.
        var maxValueMember = members.FirstOrDefault(m => m.Name == "MaxValue");
        await Assert.That(maxValueMember).IsNotNull();
    }

    [Test]
    public async Task Int32Type_HasMethods() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var intType = registry.GetTypeDefinition<int>();

        var methods = intType.Methods.ToList();

        await Assert.That(methods.Count != 0).IsTrue();
        // Int32 has ToString, etc.
        var toStringMethod = methods.FirstOrDefault(m => m.Name == "ToString");
        await Assert.That(toStringMethod).IsNotNull();
    }

    [Test]
    public async Task GetMember_ReturnsCorrectMember() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var intType = registry.GetTypeDefinition<int>();

        var maxValueMember = intType.GetMember("MaxValue");

        await Assert.That(maxValueMember).IsNotNull();
    await Assert.That(maxValueMember!.Name).IsEqualTo("MaxValue");
    }

    [Test]
    public async Task GetMember_ReturnsNullForNonExistentMember() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var intType = registry.GetTypeDefinition<int>();

        var nonExistentMember = intType.GetMember("NonExistent");

        await Assert.That(nonExistentMember).IsNull();
    }

    [Test]
    public async Task GenericType_HasCorrectProperties() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var listType = registry.GetTypeDefinition<List<int>>();

        await Assert.That(listType.Name).Contains("List");
        await Assert.That(listType.Namespace).IsEqualTo("System.Collections.Generic");
        await Assert.That(listType.FullName).Contains("System.Collections.Generic.List");
    }

    [Test]
    public async Task GenericType_HasMembers() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var listType = registry.GetTypeDefinition<List<int>>();

        var members = listType.Members.ToList();
        await Assert.That(members.Count > 0).IsTrue();
        
        // List<T> has Count property
        var countMember = members.FirstOrDefault(m => m.Name == "Count");
        await Assert.That(countMember).IsNotNull();
    }

    [Test]
    public async Task NestedType_HasCorrectProperties() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var nestedType = registry.GetTypeDefinition<TestOuterClass.TestNestedClass>();

        await Assert.That(nestedType.Name).Contains("TestNestedClass");
        await Assert.That(nestedType.FullName).Contains("TestOuterClass+TestNestedClass");
    }

    [Test]
    public async Task ToString_ReturnsFullName() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var intType = registry.GetTypeDefinition<int>();

        var toStringResult = intType.ToString();

        await Assert.That(toStringResult).IsEqualTo("System.Int32");
    }

    // Helper class for nested type testing
    public class TestOuterClass {
        public class TestNestedClass {
            public int Value { get; set; }
        }
    }
}