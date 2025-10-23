using System.Linq.Expressions;

using Poly.Interpretation;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Tests.Introspection;

public class ClrTypeMemberTests {
    [Test]
    public async Task MaxValueMember_HasCorrectProperties() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var intType = registry.GetTypeDefinition<int>();
        var maxValueMember = intType.GetMember("MaxValue");

        await Assert.That(maxValueMember).IsNotNull();
        await Assert.That(maxValueMember!.Name).IsEqualTo("MaxValue");
        await Assert.That(maxValueMember.DeclaringType).IsEqualTo(intType);
        await Assert.That(maxValueMember.MemberType.FullName).IsEqualTo("System.Int32");
    }

    [Test]
    public async Task GetMemberAccessor_ReturnsValue() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var intType = registry.GetTypeDefinition<int>();
        var maxValueMember = intType.GetMember("MaxValue");
        var accessor = maxValueMember!.GetMemberAccessor(Value.Null);

        await Assert.That(accessor).IsNotNull();

        var interpretationContext = new InterpretationContext();
        var expression = accessor!.BuildExpression(interpretationContext);
        var lambda = Expression.Lambda<Func<int>>(expression).Compile();
        var result = lambda();

        await Assert.That(result).IsEqualTo(int.MaxValue);
    }

    [Test]
    public async Task StaticMember_IsAccessible() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var intType = registry.GetTypeDefinition<int>();
        var maxValueMember = intType.GetMember("MaxValue");

        await Assert.That(maxValueMember).IsNotNull();
        await Assert.That(maxValueMember!.Name).IsEqualTo("MaxValue");
        
        // Static members should be accessible
        var accessor = maxValueMember.GetMemberAccessor(Value.Null);
        await Assert.That(accessor).IsNotNull();
    }

    [Test]
    public async Task InstanceMember_IsAccessible() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var stringType = registry.GetTypeDefinition<string>();
        var lengthMember = stringType.GetMember("Length");

        await Assert.That(lengthMember).IsNotNull();
        await Assert.That(lengthMember!.Name).IsEqualTo("Length");
        
        // Instance members should be accessible with an instance
        var testString = "test";
        var stringValue = new Literal(testString);
        var accessor = lengthMember.GetMemberAccessor(stringValue);
        await Assert.That(accessor).IsNotNull();
    }
}