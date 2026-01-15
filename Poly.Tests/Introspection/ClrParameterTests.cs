using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Tests.Introspection;

public class ClrParameterTests {
    [Test]
    public async Task SubstringMethod_ParametersHaveCorrectProperties()
    {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var stringType = registry.GetTypeDefinition<string>();
        var substringMethod = stringType.Methods.First(m => m.Name == "Substring" && m.Parameters.Count() == 2);

        var parameters = substringMethod.Parameters.ToArray();

        await Assert.That(parameters.Length).IsEqualTo(2);
        var startIndexParam = parameters[0];
        await Assert.That(startIndexParam.Name).IsEqualTo("startIndex");
        await Assert.That(startIndexParam.Position).IsEqualTo(0);
        await Assert.That(startIndexParam.ParameterTypeDefinition.FullName).IsEqualTo("System.Int32");
        await Assert.That(startIndexParam.IsOptional).IsFalse();

        var lengthParam = parameters[1];
        await Assert.That(lengthParam.Name).IsEqualTo("length");
        await Assert.That(lengthParam.Position).IsEqualTo(1);
        await Assert.That(lengthParam.ParameterTypeDefinition.FullName).IsEqualTo("System.Int32");
        await Assert.That(lengthParam.IsOptional).IsFalse();
    }

    [Test]
    public async Task OptionalParameter_HasCorrectProperties()
    {
        // Find a method with optional parameter, e.g., string.PadLeft(int totalWidth, char paddingChar = ' ')
        var registry = ClrTypeDefinitionRegistry.Shared;
        var typeDefinition = registry.GetTypeDefinition<ClrParameterTests>();
        var padLeftMethod = typeDefinition.Methods.First(m => m.Name == nameof(DummyMethod) && m.Parameters.Count() == 2);

        var parameters = padLeftMethod.Parameters;
        var requiredParam = parameters.First();
        await Assert.That(requiredParam.IsOptional).IsFalse();
        await Assert.That(requiredParam.DefaultValue).IsEqualTo(DBNull.Value);

        var optionalParam = parameters.Last();
        await Assert.That(optionalParam.IsOptional).IsTrue();
        await Assert.That(optionalParam.DefaultValue).IsEqualTo("default");
    }

    private static void DummyMethod(int requiredParam, string optionalParam = "default")
    {
        // This method is only for testing purposes.
    }
}