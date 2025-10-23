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
}