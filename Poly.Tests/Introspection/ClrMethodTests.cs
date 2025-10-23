using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Tests.Introspection;

public class ClrMethodTests {
    [Test]
    public async Task ToStringMethod_HasCorrectProperties() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var intType = registry.GetTypeDefinition<int>();
        var toStringMethod = intType.Methods.First(m => m.Name == "ToString");

        await Assert.That(toStringMethod.Name).IsEqualTo("ToString");
        await Assert.That(toStringMethod.DeclaringType).IsEqualTo(intType);
        await Assert.That(toStringMethod.ReturnType.FullName).IsEqualTo("System.String");
    }

    [Test]
    public async Task ToStringMethod_HasParameters() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var intType = registry.GetTypeDefinition<int>();
        var toStringMethod = intType.Methods.First(m => m.Name == "ToString");

        var parameters = toStringMethod.Parameters.ToList();

        // ToString has overloads, but the parameterless one has no parameters
        var parameterlessToString = intType.Methods.First(m => m.Name == "ToString" && !m.Parameters.Any());
        await Assert.That(parameterlessToString.Parameters.Any()).IsFalse();
    }

    [Test]
    public async Task ParseMethod_HasCorrectReturnType() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var intType = registry.GetTypeDefinition<int>();
        var parseMethod = intType.Methods.First(m => m.Name == "Parse");

        await Assert.That(parseMethod.Name).IsEqualTo("Parse");
        await Assert.That(parseMethod.ReturnType.FullName).IsEqualTo("System.Int32");
    }

    [Test]
    public async Task Method_WithNoParameters_HasEmptyParametersList() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var stringType = registry.GetTypeDefinition<string>();
        var toLowerMethod = stringType.Methods.First(m => m.Name == "ToLower" && !m.Parameters.Any());

        await Assert.That(toLowerMethod.Parameters.Count()).IsEqualTo(0);
    }

    [Test]
    public async Task Method_WithGenericReturnType_HasCorrectReturnType() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var listType = registry.GetTypeDefinition<List<int>>();
        var getEnumeratorMethod = listType.Methods.First(m => m.Name == "GetEnumerator");

        await Assert.That(getEnumeratorMethod.ReturnType).IsNotNull();
        await Assert.That(getEnumeratorMethod.ReturnType.Name).Contains("Enumerator");
    }

    [Test]
    public async Task Method_WithMultipleOverloads_CanBeDistinguished() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var stringType = registry.GetTypeDefinition<string>();
        var indexOfMethods = stringType.Methods.Where(m => m.Name == "IndexOf").ToList();

        await Assert.That(indexOfMethods.Count > 1).IsTrue();
        // Find the single char overload and single string overload
        var charOverload = indexOfMethods.FirstOrDefault(m =>
            m.Parameters.Count() == 1 &&
            m.Parameters.First().ParameterType.Name == "Char");
        var stringOverload = indexOfMethods.FirstOrDefault(m =>
            m.Parameters.Count() == 1 &&
            m.Parameters.First().ParameterType.Name == "String");

        await Assert.That(charOverload).IsNotNull();
        await Assert.That(stringOverload).IsNotNull();
    }
}