using System.Linq.Expressions;

using Poly.Interpretation;
using Poly.Introspection.CommonLanguageRuntime;
using Poly.Introspection.Extensions;

namespace Poly.Tests.Introspection;

public class ClrTypeComplexScenariosTests {
    [Test]
    public async Task ChainedPropertyAccess_SimpleChain() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var personType = registry.GetTypeDefinition<Person>();
        
        var addressMembers = personType.GetMembers("Address");
        await Assert.That(addressMembers.Count()).IsGreaterThan(0);
        
        var addressProperty = addressMembers.First();
        var person = new Person { Address = new Address { City = "Seattle" } };
        var personLiteral = new Literal(person);
        
        var addressAccessor = addressProperty.GetMemberAccessor(personLiteral);
        var context = new InterpretationContext();
        var addressExpression = addressAccessor.BuildExpression(context);
        var addressLambda = Expression.Lambda<Func<Address>>(addressExpression).Compile();
        var resultAddress = addressLambda();
        
        await Assert.That(resultAddress).IsNotNull();
        await Assert.That(resultAddress.City).IsEqualTo("Seattle");
    }

    [Test]
    public async Task ChainedPropertyAccess_TwoLevels() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var personType = registry.GetTypeDefinition<Person>();
        var addressType = registry.GetTypeDefinition<Address>();
        
        // Person -> Address -> City
        var addressMembers = personType.GetMembers("Address");
        var addressProperty = addressMembers.First();
        
        var cityMembers = addressType.GetMembers("City");
        var cityProperty = cityMembers.First();
        
        var person = new Person { Address = new Address { City = "Portland" } };
        var personLiteral = new Literal(person);
        
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
    public async Task MethodCall_WithSingleStringArgument() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var stringType = registry.GetTypeDefinition<string>();
        
        var members = stringType.GetMembers("StartsWith");
        var startWithMethod = members.WithParameters(stringType);
        
        await Assert.That(startWithMethod).IsNotNull();
        
        var testString = "hello world";
        var stringLiteral = new Literal(testString);
        var prefixLiteral = new Literal("hello");
        
        var context = new InterpretationContext();
        var methodAccessor = startWithMethod!.GetMemberAccessor(stringLiteral, [prefixLiteral]);
        var expression = methodAccessor.BuildExpression(context);
        var lambda = Expression.Lambda<Func<bool>>(expression).Compile();
        var result = lambda();
        
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task MethodCall_WithSingleArgument() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var stringType = registry.GetTypeDefinition<string>();
        var charType = registry.GetTypeDefinition<char>();
        
        var members = stringType.GetMembers("Contains");
        var containsMethod = members.WithParameters(charType);
        
        await Assert.That(containsMethod).IsNotNull();
        
        var testString = "hello";
        var stringLiteral = new Literal(testString);
        var charLiteral = new Literal('e');
        
        var context = new InterpretationContext();
        var expression = containsMethod!.GetMemberAccessor(stringLiteral, [charLiteral]).BuildExpression(context);
        var lambda = Expression.Lambda<Func<bool>>(expression).Compile();
        var result = lambda();
        
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task MethodCall_WithMultipleArguments() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var stringType = registry.GetTypeDefinition<string>();
        var intType = registry.GetTypeDefinition<int>();
        
        var members = stringType.GetMembers("Substring");
        var substringMethod = members.WithParameters(intType, intType);
        
        await Assert.That(substringMethod).IsNotNull();
        
        var testString = "hello world";
        var stringLiteral = new Literal(testString);
        var start = new Literal(0);
        var length = new Literal(5);
        
        var context = new InterpretationContext();
        var expression = substringMethod!.GetMemberAccessor(stringLiteral, [start, length]).BuildExpression(context);
        var lambda = Expression.Lambda<Func<string>>(expression).Compile();
        var result = lambda();
        
        await Assert.That(result).IsEqualTo("hello");
    }

    [Test]
    public async Task MethodOverload_DifferentParameterCounts() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var stringType = registry.GetTypeDefinition<string>();
        var intType = registry.GetTypeDefinition<int>();
        
        var members = stringType.GetMembers("Substring");
        
        // Substring(int) 
        var oneParamVersion = members.WithParameters(intType);
        // Substring(int, int)
        var twoParamVersion = members.WithParameters(intType, intType);
        
        await Assert.That(oneParamVersion).IsNotNull();
        await Assert.That(twoParamVersion).IsNotNull();
        
        var testString = "hello";
        var stringLiteral = new Literal(testString);
        var context = new InterpretationContext();
        
        // Test single parameter version
        var start = new Literal(1);
        var expr1 = oneParamVersion!.GetMemberAccessor(stringLiteral, [start]).BuildExpression(context);
        var result1 = Expression.Lambda<Func<string>>(expr1).Compile()();
        
        // Test two parameter version
        var length = new Literal(3);
        var expr2 = twoParamVersion!.GetMemberAccessor(stringLiteral, [start, length]).BuildExpression(context);
        var result2 = Expression.Lambda<Func<string>>(expr2).Compile()();
        
        await Assert.That(result1).IsEqualTo("ello");
        await Assert.That(result2).IsEqualTo("ell");
    }

    [Test]
    public async Task ListGenericMethod_WithGenericTypeParameter() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var listType = registry.GetTypeDefinition<List<int>>();
        var intType = registry.GetTypeDefinition<int>();
        
        var members = listType.GetMembers("Add");
        var addMethod = members.WithParameters(intType);
        
        await Assert.That(addMethod).IsNotNull();
        
        var list = new List<int> { 1, 2 };
        var listLiteral = new Literal(list);
        var value = new Literal(3);
        
        var context = new InterpretationContext();
        var expression = addMethod!.GetMemberAccessor(listLiteral, [value]).BuildExpression(context);
        var action = Expression.Lambda<Action>(expression).Compile();
        action();
        
        await Assert.That(list.Count).IsEqualTo(3);
        await Assert.That(list[2]).IsEqualTo(3);
    }

    [Test]
    public async Task DictionaryMethod_WithGenericTypeParameters() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var dictType = registry.GetTypeDefinition<Dictionary<string, int>>();
        var stringType = registry.GetTypeDefinition<string>();
        var intType = registry.GetTypeDefinition<int>();
        
        var members = dictType.GetMembers("Add");
        var addMethod = members.WithParameters(stringType, intType);
        
        await Assert.That(addMethod).IsNotNull();
        
        var dict = new Dictionary<string, int> { ["one"] = 1 };
        var dictLiteral = new Literal(dict);
        var key = new Literal("two");
        var value = new Literal(2);
        
        var context = new InterpretationContext();
        var expression = addMethod!.GetMemberAccessor(dictLiteral, [key, value]).BuildExpression(context);
        var action = Expression.Lambda<Action>(expression).Compile();
        action();
        
        await Assert.That(dict.Count).IsEqualTo(2);
        await Assert.That(dict["two"]).IsEqualTo(2);
    }

    [Test]
    public async Task ConditionalPropertyAccess_WithNullCheck() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var personType = registry.GetTypeDefinition<Person>();
        var addressMembers = personType.GetMembers("Address");
        var addressProperty = addressMembers.First();
        
        var person = new Person { Address = null };
        var personLiteral = new Literal(person);
        
        var context = new InterpretationContext();
        var addressAccessor = addressProperty.GetMemberAccessor(personLiteral);
        var addressExpression = addressAccessor.BuildExpression(context);
        var addressLambda = Expression.Lambda<Func<Address?>>(addressExpression).Compile();
        var result = addressLambda();
        
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task MultipleFieldAccess() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var personType = registry.GetTypeDefinition<PersonWithFields>();
        
        var firstNameMembers = personType.GetMembers("FirstName");
        var lastNameMembers = personType.GetMembers("LastName");
        
        await Assert.That(firstNameMembers.Count()).IsGreaterThan(0);
        await Assert.That(lastNameMembers.Count()).IsGreaterThan(0);
        
        var person = new PersonWithFields { FirstName = "John", LastName = "Doe" };
        var personLiteral = new Literal(person);
        var context = new InterpretationContext();
        
        var firstAccessor = firstNameMembers.First().GetMemberAccessor(personLiteral);
        var firstExpr = firstAccessor.BuildExpression(context);
        var firstName = Expression.Lambda<Func<string>>(firstExpr).Compile()();
        
        var lastAccessor = lastNameMembers.First().GetMemberAccessor(personLiteral);
        var lastExpr = lastAccessor.BuildExpression(context);
        var lastName = Expression.Lambda<Func<string>>(lastExpr).Compile()();
        
        await Assert.That(firstName).IsEqualTo("John");
        await Assert.That(lastName).IsEqualTo("Doe");
    }

    [Test]
    public async Task PropertyAndMethodCombination() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var stringType = registry.GetTypeDefinition<string>();
        
        // Get length property and use it
        var lengthMembers = stringType.GetMembers("Length");
        var lengthProperty = lengthMembers.First();
        
        var testString = "hello";
        var stringLiteral = new Literal(testString);
        var context = new InterpretationContext();
        
        var lengthAccessor = lengthProperty.GetMemberAccessor(stringLiteral);
        var lengthExpr = lengthAccessor.BuildExpression(context);
        var length = Expression.Lambda<Func<int>>(lengthExpr).Compile()();
        
        await Assert.That(length).IsEqualTo(5);
    }

    [Test]
    public async Task DifferentInstanceTypes_SameMethod() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var stringType = registry.GetTypeDefinition<string>();
        var charType = registry.GetTypeDefinition<char>();
        
        var members = stringType.GetMembers("IndexOf");
        var indexOfMethod = members.WithParameters(charType);
        
        await Assert.That(indexOfMethod).IsNotNull();
        
        var context = new InterpretationContext();
        
        // Test with different string values
        var testCases = new[] { ("hello", 'e', 1), ("world", 'w', 0), ("testing", 't', 0) };
        foreach (var (testString, searchChar, expectedIndex) in testCases) {
            var stringLiteral = new Literal(testString);
            var charLiteral = new Literal(searchChar);
            var accessor = indexOfMethod!.GetMemberAccessor(stringLiteral, [charLiteral]);
            var expr = accessor.BuildExpression(context);
            var result = Expression.Lambda<Func<int>>(expr).Compile()();
            
            await Assert.That(result).IsEqualTo(expectedIndex);
        }
    }

    [Test]
    public async Task NestedListOperations() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var listType = registry.GetTypeDefinition<List<int>>();
        var intType = registry.GetTypeDefinition<int>();
        
        var addMembers = listType.GetMembers("Add");
        var addMethod = addMembers.WithParameters(intType);
        
        var list = new List<int>();
        var listLiteral = new Literal(list);
        var context = new InterpretationContext();
        
        // Add multiple values
        foreach (var value in new[] { 1, 2, 3, 4, 5 }) {
            var valueLiteral = new Literal(value);
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
