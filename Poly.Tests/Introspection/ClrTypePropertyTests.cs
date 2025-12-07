using System.Linq.Expressions;

using Poly.Interpretation;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Tests.Introspection;

public class ClrTypePropertyTests {
    [Test]
    public async Task LengthProperty_HasCorrectProperties() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var stringType = registry.GetTypeDefinition<string>();
        var lengthProperty = stringType.GetMembers("Length").SingleOrDefault();

        await Assert.That(lengthProperty).IsNotNull();
        await Assert.That(lengthProperty).IsTypeOf<ClrTypeProperty>();
        await Assert.That(lengthProperty!.Name).IsEqualTo("Length");
        await Assert.That(lengthProperty.DeclaringType).IsEqualTo(stringType);
        await Assert.That(lengthProperty.MemberType.FullName).IsEqualTo("System.Int32");
    }

    [Test]
    public async Task Property_GetMemberAccessor_ReturnsValue() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var stringType = registry.GetTypeDefinition<string>();
        var lengthProperty = stringType.GetMembers("Length").SingleOrDefault();

        var testString = "Hello World";
        var stringValue = Value.Wrap(testString);
        var accessor = lengthProperty!.GetMemberAccessor(stringValue);

        await Assert.That(accessor).IsNotNull();

        var interpretationContext = new InterpretationContext();
        var expression = accessor.BuildExpression(interpretationContext);
        var lambda = Expression.Lambda<Func<int>>(expression).Compile();
        var result = lambda();

        await Assert.That(result).IsEqualTo(testString.Length);
    }

    [Test]
    public async Task Property_ToString_HasCorrectFormat() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var stringType = registry.GetTypeDefinition<string>();
        var lengthProperty = stringType.GetMembers("Length").SingleOrDefault() as ClrTypeProperty;

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
        var nowProperty = dateTimeType.GetMembers("Now").SingleOrDefault();

        await Assert.That(nowProperty).IsNotNull();
        await Assert.That(nowProperty).IsTypeOf<ClrTypeProperty>();
        await Assert.That(nowProperty!.Name).IsEqualTo("Now");
        await Assert.That(nowProperty.MemberType.FullName).IsEqualTo("System.DateTime");
    }

    [Test]
    public async Task InstanceProperty_GetMemberAccessor_ReturnsCorrectValue() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var dateTimeType = registry.GetTypeDefinition<DateTime>();
        var dayProperty = dateTimeType.GetMembers("Day").SingleOrDefault();

        var testDate = new DateTime(2025, 10, 23);
        var dateValue = Value.Wrap(testDate);
        var accessor = dayProperty!.GetMemberAccessor(dateValue);

        await Assert.That(accessor).IsNotNull();

        var interpretationContext = new InterpretationContext();
        var expression = accessor.BuildExpression(interpretationContext);
        var lambda = Expression.Lambda<Func<int>>(expression).Compile();
        var result = lambda();

        await Assert.That(result).IsEqualTo(23);
    }

    [Test]
    public async Task StaticProperty_GetMemberAccessor_ReturnsCorrectValue() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var dateTimeType = registry.GetTypeDefinition<DateTime>();
        var utcNowProperty = dateTimeType.GetMembers("UtcNow").SingleOrDefault();

        var accessor = utcNowProperty!.GetMemberAccessor(Value.Null);

        await Assert.That(accessor).IsNotNull();

        var interpretationContext = new InterpretationContext();
        var expression = accessor.BuildExpression(interpretationContext);
        var lambda = Expression.Lambda<Func<DateTime>>(expression).Compile();
        var result = lambda();

        await Assert.That(result).IsLessThanOrEqualTo(DateTime.UtcNow);
    }
}