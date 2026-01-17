using System.Linq.Expressions;

using Poly.Interpretation;
using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Tests.Introspection;

public class ClrTypeComplexScenariosTests {
    [Test]
    public async Task ChainedPropertyAccess_SimpleChain()
    {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var personType = registry.GetTypeDefinition<Person>();

        var addressMembers = personType.Properties.WithName("Address");
        await Assert.That(addressMembers.Count()).IsGreaterThan(0);

        var addressProperty = addressMembers.First();
        var person = new Person { Address = new Address { City = "Seattle" } };
        var personLiteral = Value.Wrap(person);

        var addressAccessor = addressProperty.GetMemberAccessor(personLiteral);
        var context = new InterpretationContext();
        var addressExpression = addressAccessor.BuildExpression(context);
        var addressLambda = Expression.Lambda<Func<Address>>(addressExpression).Compile();
        var resultAddress = addressLambda();

        await Assert.That(resultAddress).IsNotNull();
        await Assert.That(resultAddress.City).IsEqualTo("Seattle");
    }

    [Test]
    public async Task ChainedPropertyAccess_TwoLevels()
    {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var personType = registry.GetTypeDefinition<Person>();
        var addressType = registry.GetTypeDefinition<Address>();

        // Person -> Address -> City
        var addressMembers = personType.Properties.WithName("Address");
        await Assert.That(addressMembers).IsNotNull();
        await Assert.That(addressMembers).HasSingleItem();
        var addressProperty = addressMembers.First();

        var cityMembers = addressType.Properties.WithName("City");
        await Assert.That(cityMembers).IsNotNull();
        await Assert.That(cityMembers).HasSingleItem();
        var cityProperty = cityMembers.First();

        var person = new Person { Address = new Address { City = "Portland" } };
        var personLiteral = Value.Wrap(person);

        // First get address via property accessor
        var addressAccessor = addressProperty.GetMemberAccessor(personLiteral);
        var context = new InterpretationContext();
        var addressExpression = addressAccessor.BuildExpression(context);
        var addressLambda = Expression.Lambda<Func<Address?>>(addressExpression).Compile();
        var resultAddress = addressLambda();

        // Then get city from the address we got
        await Assert.That(resultAddress).IsNotNull();
        await Assert.That(resultAddress!.City).IsEqualTo("Portland");
    }

    [Test]
    public async Task MethodCall_WithSingleStringArgument()
    {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var stringType = registry.GetTypeDefinition<string>();

        var members = stringType.Methods.WithName("StartsWith");
        var startsWithMethods = members.WithParameterTypes(stringType);

        await Assert.That(startsWithMethods).HasSingleItem();
        var startsWithMethod = startsWithMethods.First();

        var testString = "hello world";
        var stringLiteral = Value.Wrap(testString);
        var prefixLiteral = Value.Wrap("hello");

        var context = new InterpretationContext();
        var methodAccessor = startsWithMethod!.GetMemberAccessor(stringLiteral, [prefixLiteral]);
        var expression = methodAccessor.BuildExpression(context);
        var lambda = Expression.Lambda<Func<bool>>(expression).Compile();
        var result = lambda();

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task MethodCall_WithSingleArgument()
    {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var stringType = registry.GetTypeDefinition<string>();
        var charType = registry.GetTypeDefinition<char>();

        var members = stringType.Methods.WithName("Contains");
        var containsMethods = members.WithParameterTypes(charType);

        await Assert.That(containsMethods).HasSingleItem();
        var containsMethod = containsMethods.First();

        var testString = "hello";
        var stringLiteral = Value.Wrap(testString);
        var charLiteral = Value.Wrap('e');

        var context = new InterpretationContext();
        var expression = containsMethod!.GetMemberAccessor(stringLiteral, [charLiteral]).BuildExpression(context);
        var lambda = Expression.Lambda<Func<bool>>(expression).Compile();
        var result = lambda();

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task MethodCall_WithMultipleArguments()
    {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var stringType = registry.GetTypeDefinition<string>();
        var intType = registry.GetTypeDefinition<int>();

        var members = stringType.Methods.WithName("Substring");
        var substringMethods = members.WithParameterTypes(intType, intType);
        await Assert.That(substringMethods).HasSingleItem();
        var substringMethod = substringMethods.First();

        await Assert.That(substringMethod).IsNotNull();

        var testString = "hello world";
        var stringLiteral = Value.Wrap(testString);
        var start = Value.Wrap(0);
        var length = Value.Wrap(5);

        var context = new InterpretationContext();
        var expression = substringMethod!.GetMemberAccessor(stringLiteral, [start, length]).BuildExpression(context);
        var lambda = Expression.Lambda<Func<string>>(expression).Compile();
        var result = lambda();

        await Assert.That(result).IsEqualTo("hello");
    }

    [Test]
    public async Task MethodOverload_DifferentParameterCounts()
    {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var stringType = registry.GetTypeDefinition<string>();
        var intType = registry.GetTypeDefinition<int>();

        var members = stringType.Methods.WithName("Substring");

        // Substring(int) 
        var oneParamMethods = members.WithParameterTypes(intType);
        await Assert.That(oneParamMethods).HasSingleItem();
        var oneParamVersion = oneParamMethods.First();
        // Substring(int, int)
        var twoParamMethods = members.WithParameterTypes(intType, intType);
        await Assert.That(twoParamMethods).HasSingleItem();
        var twoParamVersion = twoParamMethods.First();

        await Assert.That(oneParamVersion).IsNotNull();
        await Assert.That(twoParamVersion).IsNotNull();

        var testString = "hello";
        var stringLiteral = Value.Wrap(testString);
        var context = new InterpretationContext();

        // Test single parameter version
        var start = Value.Wrap(1);
        var expr1 = oneParamVersion!.GetMemberAccessor(stringLiteral, [start]).BuildExpression(context);
        var result1 = Expression.Lambda<Func<string>>(expr1).Compile()();

        // Test two parameter version
        var length = Value.Wrap(3);
        var expr2 = twoParamVersion!.GetMemberAccessor(stringLiteral, [start, length]).BuildExpression(context);
        var result2 = Expression.Lambda<Func<string>>(expr2).Compile()();

        await Assert.That(result1).IsEqualTo("ello");
        await Assert.That(result2).IsEqualTo("ell");
    }

    [Test]
    public async Task ListGenericMethod_WithGenericTypeParameter()
    {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var listType = registry.GetTypeDefinition<List<int>>();
        var intType = registry.GetTypeDefinition<int>();

        var members = listType.Methods.WithName("Add");
        var addMethods = members.WithParameterTypes(intType);
        await Assert.That(addMethods).HasSingleItem();
        var addMethod = addMethods.First();

        await Assert.That(addMethod).IsNotNull();

        var list = new List<int> { 1, 2 };
        var listLiteral = Value.Wrap(list);
        var value = Value.Wrap(3);

        var context = new InterpretationContext();
        var expression = addMethod!.GetMemberAccessor(listLiteral, [value]).BuildExpression(context);
        var action = Expression.Lambda<Action>(expression).Compile();
        action();

        await Assert.That(list.Count).IsEqualTo(3);
        await Assert.That(list[2]).IsEqualTo(3);
    }

    [Test]
    public async Task DictionaryMethod_WithGenericTypeParameters()
    {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var dictType = registry.GetTypeDefinition<Dictionary<string, int>>();
        var stringType = registry.GetTypeDefinition<string>();
        var intType = registry.GetTypeDefinition<int>();

        var members = dictType.Methods.WithName("Add");
        var addMethods = members.WithParameterTypes(stringType, intType);
        await Assert.That(addMethods).HasSingleItem();
        var addMethod = addMethods.First();

        await Assert.That(addMethod).IsNotNull();

        var dict = new Dictionary<string, int> { ["one"] = 1 };
        var dictLiteral = Value.Wrap(dict);
        var key = Value.Wrap("two");
        var value = Value.Wrap(2);

        var context = new InterpretationContext();
        var expression = addMethod!.GetMemberAccessor(dictLiteral, [key, value]).BuildExpression(context);
        var action = Expression.Lambda<Action>(expression).Compile();
        action();

        await Assert.That(dict.Count).IsEqualTo(2);
        await Assert.That(dict["two"]).IsEqualTo(2);
    }

    [Test]
    public async Task ConditionalPropertyAccess_WithNullCheck()
    {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var personType = registry.GetTypeDefinition<Person>();
        var addressMembers = personType.Properties.WithName("Address");
        await Assert.That(addressMembers).IsNotNull();
        await Assert.That(addressMembers).HasSingleItem();
        var addressProperty = addressMembers.First();

        var person = new Person { Address = null };
        var personLiteral = Value.Wrap(person);

        var context = new InterpretationContext();
        var addressAccessor = addressProperty.GetMemberAccessor(personLiteral);
        var addressExpression = addressAccessor.BuildExpression(context);
        var addressLambda = Expression.Lambda<Func<Address?>>(addressExpression).Compile();
        var result = addressLambda();

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task MultipleFieldAccess()
    {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var personType = registry.GetTypeDefinition<PersonWithFields>();

        var firstNameMembers = personType.Fields.WithName("FirstName");
        await Assert.That(firstNameMembers).IsNotNull();
        await Assert.That(firstNameMembers).HasSingleItem();
        var lastNameMembers = personType.Fields.WithName("LastName");
        await Assert.That(lastNameMembers).IsNotNull();
        await Assert.That(lastNameMembers).HasSingleItem();

        var person = new PersonWithFields { FirstName = "John", LastName = "Doe" };
        var personLiteral = Value.Wrap(person);
        var context = new InterpretationContext();

        var firstNameMember = firstNameMembers.First();
        var firstAccessor = firstNameMember.GetMemberAccessor(personLiteral);
        var firstExpr = firstAccessor.BuildExpression(context);
        var firstName = Expression.Lambda<Func<string>>(firstExpr).Compile()();

        var lastNameMember = lastNameMembers.First();
        var lastAccessor = lastNameMember.GetMemberAccessor(personLiteral);
        var lastExpr = lastAccessor.BuildExpression(context);
        var lastName = Expression.Lambda<Func<string>>(lastExpr).Compile()();

        await Assert.That(firstName).IsEqualTo("John");
        await Assert.That(lastName).IsEqualTo("Doe");
    }

    [Test]
    public async Task PropertyAndMethodCombination()
    {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var stringType = registry.GetTypeDefinition<string>();

        // Get length property and use it
        var lengthMembers = stringType.Properties.WithName("Length");
        await Assert.That(lengthMembers).IsNotNull();
        await Assert.That(lengthMembers).HasSingleItem();
        var lengthProperty = lengthMembers.First();

        var testString = "hello";
        var stringLiteral = Value.Wrap(testString);
        var context = new InterpretationContext();

        var lengthAccessor = lengthProperty.GetMemberAccessor(stringLiteral);
        var lengthExpr = lengthAccessor.BuildExpression(context);
        var length = Expression.Lambda<Func<int>>(lengthExpr).Compile()();

        await Assert.That(length).IsEqualTo(5);
    }

    [Test]
    public async Task DifferentInstanceTypes_SameMethod()
    {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var stringType = registry.GetTypeDefinition<string>();
        var charType = registry.GetTypeDefinition<char>();

        var members = stringType.Methods.WithName("IndexOf");
        var indexOfMethods = members.WithParameterTypes(charType);
        await Assert.That(indexOfMethods).HasSingleItem();
        var indexOfMethod = indexOfMethods.First();

        await Assert.That(indexOfMethod).IsNotNull();

        var context = new InterpretationContext();

        // Test with different string values
        var testCases = new[] { ("hello", 'e', 1), ("world", 'w', 0), ("testing", 't', 0) };
        foreach (var (testString, searchChar, expectedIndex) in testCases) {
            var stringLiteral = Value.Wrap(testString);
            var charLiteral = Value.Wrap(searchChar);
            var accessor = indexOfMethod!.GetMemberAccessor(stringLiteral, [charLiteral]);
            var expr = accessor.BuildExpression(context);
            var result = Expression.Lambda<Func<int>>(expr).Compile()();

            await Assert.That(result).IsEqualTo(expectedIndex);
        }
    }

    [Test]
    public async Task NestedListOperations()
    {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var listType = registry.GetTypeDefinition<List<int>>();
        var intType = registry.GetTypeDefinition<int>();

        var addMembers = listType.Methods.WithName("Add");
        var addMethods = addMembers.WithParameterTypes(intType);
        await Assert.That(addMethods).HasSingleItem();
        var addMethod = addMethods.First();

        var list = new List<int>();
        var listLiteral = Value.Wrap(list);
        var context = new InterpretationContext();

        // Add multiple values
        foreach (var value in new[] { 1, 2, 3, 4, 5 }) {
            var valueLiteral = Value.Wrap(value);
            var accessor = addMethod!.GetMemberAccessor(listLiteral, [valueLiteral]);
            var expr = accessor.BuildExpression(context);
            Expression.Lambda<Action>(expr).Compile()();
        }

        await Assert.That(list.Count).IsEqualTo(5);
        await Assert.That(list[0]).IsEqualTo(1);
        await Assert.That(list[4]).IsEqualTo(5);
    }

    // Helper classes
    public class Address {
        public string City { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
    }

    public class Person {
        public string Name { get; set; } = string.Empty;
        public Address? Address { get; set; }
    }

    public class PersonWithFields {
        public string FirstName = string.Empty;
        public string LastName = string.Empty;
    }
}