using Poly.Introspection.CommonLanguageRuntime;
using Poly.Introspection;

namespace Poly.Tests.Introspection;

public class EnumerableMemberExtensionsTests {
    [Test]
    public async Task WithParameters_MultipleOverloads_Distinguishable()
    {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var stringType = registry.GetTypeDefinition<string>();

        // Verify we can distinguish between different overloads by parameter types
        var charType = registry.GetTypeDefinition<char>();
        var indexOfMembers = stringType.Methods.WithName("IndexOf");

        // Find overload with char parameter
        var charVersion = indexOfMembers.WithParameterTypes(new ITypeDefinition[] { charType }).First();

        // Find overload with string parameter
        var stringVersion = indexOfMembers.WithParameterTypes(new ITypeDefinition[] { stringType }).First();

        // Verify both exist and are different
        await Assert.That(charVersion).IsNotNull();
        await Assert.That(stringVersion).IsNotNull();
        await Assert.That(charVersion!.Parameters!.First().ParameterTypeDefinition.FullName).Contains("Char");
        await Assert.That(stringVersion!.Parameters!.First().ParameterTypeDefinition.FullName).IsEqualTo("System.String");
    }

    [Test]
    public async Task WithParameters_SingleInt_FindsCorrectOverload()
    {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var stringType = registry.GetTypeDefinition<string>();
        var intType = registry.GetTypeDefinition<int>();
        var members = stringType.Methods.WithName("Substring");

        // String.Substring(int) - need to pass ITypeDefinition, not System.Type
        var filtered = members.WithParameterTypes(new ITypeDefinition[] { intType }).First();

        await Assert.That(filtered).IsNotNull();
        await Assert.That(filtered!.Parameters?.Count()).IsEqualTo(1);
    }

    [Test]
    public async Task WithParameters_IntInt_FindsCorrectOverload()
    {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var stringType = registry.GetTypeDefinition<string>();
        var intType = registry.GetTypeDefinition<int>();
        var members = stringType.Methods.WithName("Substring");

        // String.Substring(int, int) overload
        var filteredMethods = members.WithParameterTypes(new ITypeDefinition[] { intType, intType });
        await Assert.That(filteredMethods).HasSingleItem();
        var filtered = filteredMethods.First();

        await Assert.That(filtered).IsNotNull();
        await Assert.That(filtered!.Parameters?.Count()).IsEqualTo(2);
    }

    [Test]
    public async Task WithParameters_NoMatch_ReturnsEmpty()
    {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var stringType = registry.GetTypeDefinition<string>();
        var doubleType = registry.GetTypeDefinition<double>();
        var members = stringType.Methods.WithName("Substring");

        // Try to find with non-existent parameter signature
        var filtered = members.WithParameterTypes(new ITypeDefinition[] { doubleType });

        await Assert.That(filtered).IsNotNull();
        await Assert.That(filtered).IsEmpty();
    }

    [Test]
    public async Task WithParameters_OnField_ReturnsEmpty()
    {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var intType = registry.GetTypeDefinition<int>();
        var members = intType.Fields.WithName("MaxValue");

        // MaxValue is a field, not a method, so filtering by parameters returns empty
        var filtered = members.WithParameterTypes(new ITypeDefinition[] { intType });

        await Assert.That(filtered).IsNotNull();
        await Assert.That(filtered).IsEmpty();
    }

    [Test]
    public async Task WithParameters_Property_ReturnsEmpty()
    {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var stringType = registry.GetTypeDefinition<string>();
        var intParamType = registry.GetTypeDefinition<int>();
        var members = stringType.Properties.WithName("Length");

        // Length is a property, not a method, so filtering by parameters returns empty
        var filtered = members.WithParameterTypes(new ITypeDefinition[] { intParamType });

        await Assert.That(filtered).IsNotNull();
        await Assert.That(filtered).IsEmpty();
    }

    [Test]
    public async Task WithParameters_IndexOf_CharOverload()
    {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var stringType = registry.GetTypeDefinition<string>();
        var charType = registry.GetTypeDefinition<char>();
        var members = stringType.Methods.WithName("IndexOf");

        // String.IndexOf(char)
        var filtered = members.WithParameterTypes(new ITypeDefinition[] { charType }).First();

        await Assert.That(filtered).IsNotNull();
        await Assert.That(filtered!.Parameters?.Count()).IsEqualTo(1);
        var paramType = filtered.Parameters!.First().ParameterTypeDefinition;
        await Assert.That(paramType.FullName).Contains("Char");
    }

    [Test]
    public async Task WithParameters_IndexOf_StringOverload()
    {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var stringType = registry.GetTypeDefinition<string>();
        var members = stringType.Methods.WithName("IndexOf");

        // String.IndexOf(string)
        var filtered = members.WithParameterTypes(new ITypeDefinition[] { stringType }).First();

        await Assert.That(filtered).IsNotNull();
        await Assert.That(filtered!.Parameters?.Count()).IsEqualTo(1);
        var paramType = filtered.Parameters!.First().ParameterTypeDefinition;
        await Assert.That(paramType.FullName).IsEqualTo("System.String");
    }

    [Test]
    public async Task WithParameters_Contains_SingleStringOverload()
    {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var stringType = registry.GetTypeDefinition<string>();
        var members = stringType.Methods.WithName("Contains");

        // String.Contains(string)
        var filtered = members.WithParameterTypes(new ITypeDefinition[] { stringType }).First();

        await Assert.That(filtered).IsNotNull();
        await Assert.That(filtered!.Parameters?.Count()).IsEqualTo(1);
    }

    [Test]
    public async Task WithParameters_Contains_WithStringComparison()
    {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var stringType = registry.GetTypeDefinition<string>();
        var stringComparisonType = registry.GetTypeDefinition<StringComparison>();
        var members = stringType.Methods.WithName("Contains");

        // String.Contains(string, StringComparison)
        var filtered = members.WithParameterTypes(new ITypeDefinition[] { stringType, stringComparisonType }).First();

        await Assert.That(filtered).IsNotNull();
        await Assert.That(filtered!.Parameters?.Count()).IsEqualTo(2);
    }

    [Test]
    public async Task WithParameters_Replace_StringOverload()
    {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var stringType = registry.GetTypeDefinition<string>();
        var members = stringType.Methods.WithName("Replace");

        // String.Replace(string, string)
        var filtered = members.WithParameterTypes(new ITypeDefinition[] { stringType, stringType }).First();

        await Assert.That(filtered).IsNotNull();
        await Assert.That(filtered!.Parameters?.Count()).IsEqualTo(2);
        var firstParam = filtered.Parameters!.First();
        await Assert.That(firstParam.ParameterTypeDefinition.FullName).IsEqualTo("System.String");
    }

    [Test]
    public async Task WithParameters_Replace_CharOverload()
    {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var charType = registry.GetTypeDefinition<char>();
        var members = registry.GetTypeDefinition<string>().Methods.WithName("Replace");

        // String.Replace(char, char)
        var filtered = members.WithParameterTypes(new ITypeDefinition[] { charType, charType }).First();

        await Assert.That(filtered).IsNotNull();
        await Assert.That(filtered!.Parameters?.Count()).IsEqualTo(2);
        var firstParam = filtered.Parameters!.First();
        await Assert.That(firstParam.ParameterTypeDefinition.FullName).Contains("Char");
    }

    [Test]
    public async Task WithParameters_ListAdd_ByType()
    {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var listType = registry.GetTypeDefinition<List<string>>();
        var stringType = registry.GetTypeDefinition<string>();
        var members = listType.Methods.WithName("Add");

        // List<string>.Add(string)
        var filtered = members.WithParameterTypes(new ITypeDefinition[] { stringType }).First();

        await Assert.That(filtered).IsNotNull();
        await Assert.That(filtered!.Parameters?.Count()).IsEqualTo(1);
    }

    [Test]
    public async Task WithParameters_StartsWith()
    {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var stringType = registry.GetTypeDefinition<string>();
        var members = stringType.Methods.WithName("StartsWith");

        // String.StartsWith(string)
        var filtered = members.WithParameterTypes(new ITypeDefinition[] { stringType }).First();

        await Assert.That(filtered).IsNotNull();
        await Assert.That(filtered!.Parameters?.Count()).IsEqualTo(1);
    }

    [Test]
    public async Task WithParameters_DistinguishesOverloads()
    {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var stringType = registry.GetTypeDefinition<string>();
        var charType = registry.GetTypeDefinition<char>();

        var members = stringType.Methods.WithName("IndexOf");

        // Can distinguish between IndexOf(char) and IndexOf(string)
        var charFiltered = members.WithParameterTypes(new ITypeDefinition[] { charType }).First();
        var stringFiltered = members.WithParameterTypes(new ITypeDefinition[] { stringType }).First();

        await Assert.That(charFiltered).IsNotNull();
        await Assert.That(stringFiltered).IsNotNull();

        // Verify they're actually different
        var charParam = charFiltered!.Parameters!.First();
        var stringParam = stringFiltered!.Parameters!.First();

        await Assert.That(charParam.ParameterTypeDefinition.FullName).Contains("Char");
        await Assert.That(stringParam.ParameterTypeDefinition.FullName).IsEqualTo("System.String");
    }
}