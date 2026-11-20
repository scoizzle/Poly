using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Tests.Introspection;

/// <summary>
/// The CLR registry's overload matching and ground-type facts (#998 T12b moved these out of the retired provider-collection
/// test; their subject is the CLR registry, not the retired collection).
/// </summary>
public class ClrDefinitionFactTests {
    [Test]
    public async Task FindMatchingMethodOverloads_SingleOverload_ReturnsMethod() {
        var registry = new ClrTypeDefinitionRegistry();
        var stringType = registry.GetTypeDefinition<string>();

        var methods = stringType.FindMatchingMethodOverloads("ToString", [], Poly.Interpretation.CSharp.CSharpTypeSystem.Shared);

        await Assert.That(methods).HasSingleItem();
        await Assert.That(methods.Single().Name).IsEqualTo("ToString");
    }

    [Test]
    public async Task FindMatchingMethodOverloads_MultipleOverloads_ReturnsBestMatch() {
        var registry = new ClrTypeDefinitionRegistry();
        var stringType = registry.GetTypeDefinition<string>();
        var charType = registry.GetTypeDefinition<char>();

        var methods = stringType.FindMatchingMethodOverloads("IndexOf", [charType], Poly.Interpretation.CSharp.CSharpTypeSystem.Shared);

        await Assert.That(methods).HasSingleItem();
        await Assert.That(methods.Single().Name).IsEqualTo("IndexOf");
    }

    [Test]
    public async Task FindMatchingMethodOverloads_NoMatch_ReturnsEmpty() {
        var registry = new ClrTypeDefinitionRegistry();
        var stringType = registry.GetTypeDefinition<string>();

        var methods = stringType.FindMatchingMethodOverloads("NonExistentMethod", [], Poly.Interpretation.CSharp.CSharpTypeSystem.Shared);

        await Assert.That(methods).IsEmpty();
    }

    [Test]
    public async Task GroundTypeFacts_AreExposedForClrTypes() {
        var registry = new ClrTypeDefinitionRegistry();

        // Test primitive types
        ITypeDefinition intType = registry.GetTypeDefinition(typeof(int));
        ITypeDefinition stringType = registry.GetTypeDefinition(typeof(string));
        ITypeDefinition dateTimeType = registry.GetTypeDefinition(typeof(DateTime));
        ITypeDefinition listType = registry.GetTypeDefinition(typeof(List<int>));

        // int is primitive
        await Assert.That(intType.GetRuntimeTypeOrThrow()).IsEqualTo(typeof(int));
        await Assert.That(ClrBuiltInRules.IsPrimitive(intType)).IsTrue();

        // string is primitive
        await Assert.That(stringType.GetRuntimeTypeOrThrow()).IsEqualTo(typeof(string));
        await Assert.That(ClrBuiltInRules.IsPrimitive(stringType)).IsTrue();

        // DateTime is primitive, not a DateTime-shaped category bit
        await Assert.That(dateTimeType.GetRuntimeTypeOrThrow()).IsEqualTo(typeof(DateTime));
        await Assert.That(ClrBuiltInRules.IsPrimitive(dateTimeType)).IsTrue();

        // List<int> is a class; its element type is structural, not a flag
        await Assert.That(ClrBuiltInRules.IsPrimitive(listType)).IsFalse();
        await Assert.That(Poly.Interpretation.CSharp.CSharpTypeSystem.Shared.HasValueSemantics(listType)).IsFalse();
        await Assert.That(Poly.Interpretation.CSharp.CSharpTypeSystem.Shared.IsEnumType(listType)).IsFalse();
        await Assert.That(listType.GetElementType(Poly.Interpretation.CSharp.CSharpTypeSystem.Shared)!.GetRuntimeTypeOrThrow()).IsEqualTo(typeof(int));
    }
}