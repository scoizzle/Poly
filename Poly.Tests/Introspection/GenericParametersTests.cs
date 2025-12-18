using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Tests.Introspection;

/// <summary>
/// Tests for ITypeDefinition.GenericParameters behavior on open/closed generics and non-generic types.
/// </summary>
public class GenericParametersTests {
    private readonly ClrTypeDefinitionRegistry _registry = ClrTypeDefinitionRegistry.Shared;

    [Test]
    public async Task GenericParameters_NonGenericType_ReturnsNull() {
        var stringType = _registry.GetTypeDefinition<string>();
        var genericParams = stringType.GenericParameters;

        await Assert.That(genericParams).IsNull();
    }

    [Test]
    public async Task GenericParameters_ClosedGenericListInt_ReturnsNonNullWithCorrectType() {
        var listIntType = _registry.GetTypeDefinition(typeof(List<int>));
        var genericParams = listIntType.GenericParameters;

        await Assert.That(genericParams).IsNotNull();

        if (genericParams is null) return;

        await Assert.That(genericParams.Count()).IsEqualTo(1);

        var param = genericParams.First();
        await Assert.That(param.Name).IsEqualTo("T");
        await Assert.That(param.Position).IsEqualTo(0);
        await Assert.That(param.ParameterTypeDefinition.Name).IsEqualTo("Int32");
    }

    [Test]
    public async Task GenericParameters_ClosedGenericDictionaryStringInt_ReturnsParametersInOrder() {
        var dictType = _registry.GetTypeDefinition(typeof(Dictionary<string, int>));
        var genericParams = dictType.GenericParameters;

        await Assert.That(genericParams).IsNotNull();

        if (genericParams is null) return;

        await Assert.That(genericParams.Count()).IsEqualTo(2);

        var paramList = genericParams.ToList();

        // First parameter: TKey -> string
        await Assert.That(paramList[0].Name).IsEqualTo("TKey");
        await Assert.That(paramList[0].Position).IsEqualTo(0);
        await Assert.That(paramList[0].ParameterTypeDefinition.Name).IsEqualTo("String");

        // Second parameter: TValue -> int
        await Assert.That(paramList[1].Name).IsEqualTo("TValue");
        await Assert.That(paramList[1].Position).IsEqualTo(1);
        await Assert.That(paramList[1].ParameterTypeDefinition.Name).IsEqualTo("Int32");
    }

    [Test]
    public async Task GenericParameters_OpenGenericList_ReturnsNonNullWithGenericParameterType() {
        var openListType = _registry.GetTypeDefinition(typeof(List<>));
        var genericParams = openListType.GenericParameters;

        await Assert.That(genericParams).IsNotNull();

        if (genericParams is null) return;

        await Assert.That(genericParams.Count()).IsEqualTo(1);

        var param = genericParams.First();
        await Assert.That(param.Name).IsEqualTo("T");
        await Assert.That(param.Position).IsEqualTo(0);
        // For open generic, the parameter type is the generic parameter placeholder itself
        await Assert.That(param.ParameterTypeDefinition.Name).IsEqualTo("T");
    }

    [Test]
    public async Task GenericParameters_OpenGenericDictionary_ReturnsParametersWithGenericPlaceholders() {
        var openDictType = _registry.GetTypeDefinition(typeof(Dictionary<,>));
        var genericParams = openDictType.GenericParameters;

        await Assert.That(genericParams).IsNotNull();

        if (genericParams is null) return;

        await Assert.That(genericParams.Count()).IsEqualTo(2);

        var paramList = genericParams.ToList();

        await Assert.That(paramList[0].Name).IsEqualTo("TKey");
        await Assert.That(paramList[0].Position).IsEqualTo(0);
        // Placeholder names are the generic parameter names themselves
        await Assert.That(paramList[0].ParameterTypeDefinition.Name).IsEqualTo("TKey");

        await Assert.That(paramList[1].Name).IsEqualTo("TValue");
        await Assert.That(paramList[1].Position).IsEqualTo(1);
        await Assert.That(paramList[1].ParameterTypeDefinition.Name).IsEqualTo("TValue");
    }

    [Test]
    public async Task GenericParameters_NestedGenericListOfListInt_ReturnsOuterParameterType() {
        var nestedType = _registry.GetTypeDefinition(typeof(List<List<int>>));
        var genericParams = nestedType.GenericParameters;

        await Assert.That(genericParams).IsNotNull();

        if (genericParams is null) return;

        await Assert.That(genericParams.Count()).IsEqualTo(1);

        var param = genericParams.First();
        await Assert.That(param.Name).IsEqualTo("T");
        // The parameter type should be List<int>
        await Assert.That(param.ParameterTypeDefinition.Name).IsEqualTo("List`1");
    }

    [Test]
    public async Task GenericParameters_ClosedGenericTuple_ReturnsAllTypeArguments() {
        var tupleType = _registry.GetTypeDefinition(typeof((string, int, double)));
        var genericParams = tupleType.GenericParameters;

        await Assert.That(genericParams).IsNotNull();

        if (genericParams is null) return;

        await Assert.That(genericParams.Count()).IsEqualTo(3);

        var paramList = genericParams.ToList();

        await Assert.That(paramList[0].ParameterTypeDefinition.Name).IsEqualTo("String");
        await Assert.That(paramList[1].ParameterTypeDefinition.Name).IsEqualTo("Int32");
        await Assert.That(paramList[2].ParameterTypeDefinition.Name).IsEqualTo("Double");
    }

    [Test]
    public async Task GenericParameters_IsCachedAndStable() {
        var listIntType = _registry.GetTypeDefinition(typeof(List<int>));
        var params1 = listIntType.GenericParameters;
        var params2 = listIntType.GenericParameters;

        // Should be the same instance (cached)
        await Assert.That(ReferenceEquals(params1, params2)).IsTrue();
    }

    [Test]
    public async Task GenericParameters_ParametersAreNonOptionalWithNoDefaults() {
        var dictType = _registry.GetTypeDefinition(typeof(Dictionary<string, int>));
        var genericParams = dictType.GenericParameters;

        if (genericParams is null) return;

        foreach (var param in genericParams) {
            await Assert.That(param.IsOptional).IsFalse();
            await Assert.That(param.DefaultValue).IsNull();
        }
    }
}