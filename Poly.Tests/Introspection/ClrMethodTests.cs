using System.Linq.Expressions;

using Poly.Interpretation;
using Poly.Introspection.CommonLanguageRuntime;
using Poly.Introspection.CommonLanguageRuntime.InterpretationHelpers;

namespace Poly.Tests.Introspection;

public class ClrMethodTests {
    private static ClrTypeDefinitionRegistry Registry => ClrTypeDefinitionRegistry.Shared;

    private static ClrMethod GetMethod<T>(string methodName, int parameterCount = -1)
        => (ClrMethod)Registry.GetTypeDefinition<T>()!.Methods.First(m =>
            m.Name == methodName &&
            (parameterCount < 0 || m.Parameters.Count() == parameterCount));

    private static ClrMethod GetMethod<T>(string methodName, Func<ClrMethod, bool> predicate)
        => (ClrMethod)Registry.GetTypeDefinition<T>()!.Methods.First(m =>
            m.Name == methodName && predicate((ClrMethod)m));

    [Test]
    public async Task ToStringMethod_HasCorrectProperties() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var intType = registry.GetTypeDefinition<int>();
        var toStringMethod = intType.Methods.First(m => m.Name == "ToString");

        await Assert.That(toStringMethod.Name).IsEqualTo("ToString");
        await Assert.That(toStringMethod.DeclaringType).IsEqualTo(intType);
        await Assert.That(toStringMethod.MemberType.FullName).IsEqualTo("System.String");
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
    public async Task ParseMethod_HasCorrectMemberType() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var intType = registry.GetTypeDefinition<int>();
        var parseMethod = intType.Methods.First(m => m.Name == "Parse");

        await Assert.That(parseMethod.Name).IsEqualTo("Parse");
        await Assert.That(parseMethod.MemberType.FullName).IsEqualTo("System.Int32");
    }

    [Test]
    public async Task Method_WithNoParameters_HasEmptyParametersList() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var stringType = registry.GetTypeDefinition<string>();
        var toLowerMethod = stringType.Methods.First(m => m.Name == "ToLower" && !m.Parameters.Any());

        await Assert.That(toLowerMethod.Parameters.Count()).IsEqualTo(0);
    }

    [Test]
    public async Task Method_WithGenericMemberType_HasCorrectMemberType() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var listType = registry.GetTypeDefinition<List<int>>();
        var getEnumeratorMethod = listType.Methods.First(m => m.Name == "GetEnumerator");

        await Assert.That(getEnumeratorMethod.MemberType).IsNotNull();
        await Assert.That(getEnumeratorMethod.MemberType.Name).Contains("Enumerator");
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
            m.Parameters.First().ParameterTypeDefinition.Name == "Char");
        var stringOverload = indexOfMethods.FirstOrDefault(m =>
            m.Parameters.Count() == 1 &&
            m.Parameters.First().ParameterTypeDefinition.Name == "String");

        await Assert.That(charOverload).IsNotNull();
        await Assert.That(stringOverload).IsNotNull();
    }

    [Test]
    public async Task Constructor_WithValidArguments_CreatesInstance() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var stringType = registry.GetTypeDefinition<string>();
        var toLowerMethod = (ClrMethod)stringType.Methods.First(m => m.Name == "ToLower" && !m.Parameters.Any());
        var instance = Value.Wrap("HELLO");

        var invocation = new ClrMethodInvocationInterpretation(toLowerMethod, instance, []);

        await Assert.That(invocation).IsNotNull();
        await Assert.That(invocation.Method).IsEqualTo(toLowerMethod);
        await Assert.That(invocation.Instance).IsEqualTo(instance);
        await Assert.That(invocation.Arguments).IsNotNull();
    }

    [Test]
    public async Task Constructor_WithNullMethod_ThrowsArgumentNullException() {
        var instance = Value.Wrap("test");

        await Assert.That(() => new ClrMethodInvocationInterpretation(null!, instance, []))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Constructor_WithNullInstance_ThrowsArgumentNullException() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var stringType = registry.GetTypeDefinition<string>();
        var toLowerMethod = (ClrMethod)stringType.Methods.First(m => m.Name == "ToLower" && !m.Parameters.Any());

        await Assert.That(() => new ClrMethodInvocationInterpretation(toLowerMethod, null!, []))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Constructor_WithNullArguments_ThrowsArgumentNullException() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var stringType = registry.GetTypeDefinition<string>();
        var toLowerMethod = (ClrMethod)stringType.Methods.First(m => m.Name == "ToLower" && !m.Parameters.Any());
        var instance = Value.Wrap("test");

        await Assert.That(() => new ClrMethodInvocationInterpretation(toLowerMethod, instance, null!))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task GetTypeDefinition_ReturnsMethodMemberType() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var stringType = registry.GetTypeDefinition<string>();
        var toLowerMethod = (ClrMethod)stringType.Methods.First(m => m.Name == "ToLower" && !m.Parameters.Any());
        var instance = Value.Wrap("HELLO");
        var invocation = new ClrMethodInvocationInterpretation(toLowerMethod, instance, []);
        var context = new InterpretationContext();

        var typeDefinition = invocation.GetTypeDefinition(context);

        await Assert.That(typeDefinition).IsNotNull();
        await Assert.That(typeDefinition.FullName).IsEqualTo("System.String");
    }

    [Test]
    public async Task GetTypeDefinition_ForIntMethod_ReturnsInt32Type() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var intType = registry.GetTypeDefinition<int>();
        var toStringMethod = (ClrMethod)intType.Methods.First(m => m.Name == "ToString" && !m.Parameters.Any());
        var instance = Value.Wrap(42);
        var invocation = new ClrMethodInvocationInterpretation(toStringMethod, instance, []);
        var context = new InterpretationContext();

        var typeDefinition = invocation.GetTypeDefinition(context);

        await Assert.That(typeDefinition).IsNotNull();
        await Assert.That(typeDefinition.FullName).IsEqualTo("System.String");
    }

    [Test]
    public async Task BuildExpression_ForInstanceMethod_CreatesCallExpression() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var stringType = registry.GetTypeDefinition<string>();
        var toLowerMethod = (ClrMethod)stringType.Methods.First(m => m.Name == "ToLower" && !m.Parameters.Any());
        var instance = Value.Wrap("HELLO");
        var invocation = new ClrMethodInvocationInterpretation(toLowerMethod, instance, []);
        var context = new InterpretationContext();

        var expression = invocation.BuildExpression(context);

        await Assert.That(expression).IsNotNull();
        await Assert.That(expression).IsTypeOf<MethodCallExpression>();

        var methodCall = (MethodCallExpression)expression;
        await Assert.That(methodCall.Method.Name).IsEqualTo("ToLower");
        await Assert.That(methodCall.Object).IsNotNull();
    }

    [Test]
    public async Task BuildExpression_CompilesAndExecutes_InstanceMethod() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var stringType = registry.GetTypeDefinition<string>();
        var toLowerMethod = (ClrMethod)stringType.Methods.First(m => m.Name == "ToLower" && !m.Parameters.Any());
        var instance = Value.Wrap("HELLO");
        var invocation = new ClrMethodInvocationInterpretation(toLowerMethod, instance, []);
        var context = new InterpretationContext();

        var expression = invocation.BuildExpression(context);
        var lambda = Expression.Lambda<Func<string>>(expression);
        var compiled = lambda.Compile();
        var result = compiled();

        await Assert.That(result).IsEqualTo("hello");
    }

    [Test]
    public async Task BuildExpression_WithArguments_PassesArgumentsToMethod() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var stringType = registry.GetTypeDefinition<string>();
        var substringMethod = (ClrMethod)stringType.Methods.First(m =>
            m.Name == "Substring" &&
            m.Parameters.Count() == 2);
        var instance = Value.Wrap("Hello World");
        var startIndex = Value.Wrap(0);
        var length = Value.Wrap(5);
        var invocation = new ClrMethodInvocationInterpretation(substringMethod, instance, [startIndex, length]);
        var context = new InterpretationContext();

        var expression = invocation.BuildExpression(context);
        var lambda = Expression.Lambda<Func<string>>(expression);
        var compiled = lambda.Compile();
        var result = compiled();

        await Assert.That(result).IsEqualTo("Hello");
    }

    [Test]
    public async Task BuildExpression_WithMultipleArguments_WorksCorrectly() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var stringType = registry.GetTypeDefinition<string>();
        var indexOfMethod = (ClrMethod)stringType.Methods.First(m =>
            m.Name == "IndexOf" &&
            m.Parameters.Count() == 1 &&
            m.Parameters.First().ParameterTypeDefinition.Name == "Char");
        var instance = Value.Wrap("Hello World");
        var searchChar = Value.Wrap('o');
        var invocation = new ClrMethodInvocationInterpretation(indexOfMethod, instance, [searchChar]);
        var context = new InterpretationContext();

        var expression = invocation.BuildExpression(context);
        var lambda = Expression.Lambda<Func<int>>(expression);
        var compiled = lambda.Compile();
        var result = compiled();

        await Assert.That(result).IsEqualTo(4);
    }

    [Test]
    public async Task BuildExpression_ForStaticMethod_WithNullInstance_CreatesStaticCallExpression() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var intType = registry.GetTypeDefinition<int>();
        var parseMethod = (ClrMethod)intType.Methods.First(m =>
            m.Name == "Parse" &&
            m.Parameters.Count() == 1 &&
            m.Parameters.First().ParameterTypeDefinition.Name == "String");
        var nullInstance = Value.Null;
        var argument = Value.Wrap("42");
        var invocation = new ClrMethodInvocationInterpretation(parseMethod, nullInstance, [argument]);
        var context = new InterpretationContext();

        var expression = invocation.BuildExpression(context);

        await Assert.That(expression).IsNotNull();
        await Assert.That(expression).IsTypeOf<MethodCallExpression>();

        var methodCall = (MethodCallExpression)expression;
        await Assert.That(methodCall.Method.Name).IsEqualTo("Parse");
        await Assert.That(methodCall.Object).IsNull(); // Static method should have null object
    }

    [Test]
    public async Task BuildExpression_StaticMethod_CompilesAndExecutes() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var intType = registry.GetTypeDefinition<int>();
        var parseMethod = (ClrMethod)intType.Methods.First(m =>
            m.Name == "Parse" &&
            m.Parameters.Count() == 1 &&
            m.Parameters.First().ParameterTypeDefinition.Name == "String");
        var nullInstance = Value.Null;
        var argument = Value.Wrap("42");
        var invocation = new ClrMethodInvocationInterpretation(parseMethod, nullInstance, [argument]);
        var context = new InterpretationContext();

        var expression = invocation.BuildExpression(context);
        var lambda = Expression.Lambda<Func<int>>(expression);
        var compiled = lambda.Compile();
        var result = compiled();

        await Assert.That(result).IsEqualTo(42);
    }

    [Test]
    public async Task BuildExpression_StaticMethodWithMultipleArgs_WorksCorrectly() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var stringType = registry.GetTypeDefinition<string>();
        var concatMethod = (ClrMethod)stringType.Methods.First(m =>
            m.Name == "Concat" &&
            m.Parameters.Count() == 2 &&
            m.Parameters.All(p => p.ParameterTypeDefinition.Name == "String"));
        var nullInstance = Value.Null;
        var arg1 = Value.Wrap("Hello");
        var arg2 = Value.Wrap(" World");
        var invocation = new ClrMethodInvocationInterpretation(concatMethod, nullInstance, [arg1, arg2]);
        var context = new InterpretationContext();

        var expression = invocation.BuildExpression(context);
        var lambda = Expression.Lambda<Func<string>>(expression);
        var compiled = lambda.Compile();
        var result = compiled();

        await Assert.That(result).IsEqualTo("Hello World");
    }

    [Test]
    public async Task BuildExpression_StaticMethodWithNonNullInstance_IgnoresInstance() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var intType = registry.GetTypeDefinition<int>();
        var parseMethod = (ClrMethod)intType.Methods.First(m =>
            m.Name == "Parse" &&
            m.Parameters.Count() == 1 &&
            m.Parameters.First().ParameterTypeDefinition.Name == "String");
        var nonNullInstance = Value.Wrap("ignored");
        var argument = Value.Wrap("123");
        var invocation = new ClrMethodInvocationInterpretation(parseMethod, nonNullInstance, [argument]);
        var context = new InterpretationContext();

        var expression = invocation.BuildExpression(context);
        var lambda = Expression.Lambda<Func<int>>(expression);
        var compiled = lambda.Compile();
        var result = compiled();

        await Assert.That(result).IsEqualTo(123);
    }

    [Test]
    public async Task BuildExpression_WithParameterAsInstance_WorksCorrectly() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var stringType = registry.GetTypeDefinition<string>();
        var toLowerMethod = (ClrMethod)stringType.Methods.First(m => m.Name == "ToLower" && !m.Parameters.Any());
        var context = new InterpretationContext();
        var parameter = context.AddParameter<string>("input");
        var invocation = new ClrMethodInvocationInterpretation(toLowerMethod, parameter, []);

        var expression = invocation.BuildExpression(context);
        var lambda = Expression.Lambda<Func<string, string>>(expression, parameter.BuildExpression(context));
        var compiled = lambda.Compile();
        var result = compiled("HELLO");

        await Assert.That(result).IsEqualTo("hello");
    }

    [Test]
    public async Task BuildExpression_WithParameterAsArgument_WorksCorrectly() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var stringType = registry.GetTypeDefinition<string>();
        var indexOfMethod = (ClrMethod)stringType.Methods.First(m =>
            m.Name == "IndexOf" &&
            m.Parameters.Count() == 1 &&
            m.Parameters.First().ParameterTypeDefinition.Name == "Char");
        var context = new InterpretationContext();
        var stringParam = context.AddParameter<string>("text");
        var charParam = context.AddParameter<char>("searchChar");
        var invocation = new ClrMethodInvocationInterpretation(indexOfMethod, stringParam, [charParam]);

        var expression = invocation.BuildExpression(context);
        var lambda = Expression.Lambda<Func<string, char, int>>(
            expression,
            stringParam.BuildExpression(context),
            charParam.BuildExpression(context));
        var compiled = lambda.Compile();
        var result = compiled("Hello World", 'W');

        await Assert.That(result).IsEqualTo(6);
    }

    [Test]
    public async Task ToString_ReturnsFormattedMethodCall() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var stringType = registry.GetTypeDefinition<string>();
        var toLowerMethod = (ClrMethod)stringType.Methods.First(m => m.Name == "ToLower" && !m.Parameters.Any());
        var instance = Value.Wrap("HELLO");
        var invocation = new ClrMethodInvocationInterpretation(toLowerMethod, instance, []);

        var result = invocation.ToString();

        await Assert.That(result).IsEqualTo("HELLO.ToLower()");
    }

    [Test]
    public async Task ToString_WithArguments_IncludesArguments() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var stringType = registry.GetTypeDefinition<string>();
        var substringMethod = (ClrMethod)stringType.Methods.First(m =>
            m.Name == "Substring" &&
            m.Parameters.Count() == 2);
        var instance = Value.Wrap("Hello World");
        var startIndex = Value.Wrap(0);
        var length = Value.Wrap(5);
        var invocation = new ClrMethodInvocationInterpretation(substringMethod, instance, [startIndex, length]);

        var result = invocation.ToString();

        await Assert.That(result).IsEqualTo("Hello World.Substring(0, 5)");
    }

    [Test]
    public async Task ToString_WithSingleArgument_FormatsCorrectly() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var stringType = registry.GetTypeDefinition<string>();
        var indexOfMethod = (ClrMethod)stringType.Methods.First(m =>
            m.Name == "IndexOf" &&
            m.Parameters.Count() == 1 &&
            m.Parameters.First().ParameterTypeDefinition.Name == "Char");
        var instance = Value.Wrap("test");
        var searchChar = Value.Wrap('e');
        var invocation = new ClrMethodInvocationInterpretation(indexOfMethod, instance, [searchChar]);

        var result = invocation.ToString();

        await Assert.That(result).IsEqualTo("test.IndexOf(e)");
    }

    [Test]
    public async Task IntegrationTest_ChainedMethodCalls_WorksCorrectly() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var stringType = registry.GetTypeDefinition<string>();
        var trimMethod = (ClrMethod)stringType.Methods.First(m => m.Name == "Trim" && !m.Parameters.Any());
        var toLowerMethod = (ClrMethod)stringType.Methods.First(m => m.Name == "ToLower" && !m.Parameters.Any());

        var context = new InterpretationContext();
        var instance = Value.Wrap("  HELLO  ");

        // First call: Trim
        var trimInvocation = new ClrMethodInvocationInterpretation(trimMethod, instance, []);

        // Second call: ToLower on result of Trim
        var toLowerInvocation = new ClrMethodInvocationInterpretation(toLowerMethod, trimInvocation, []);

        var expression = toLowerInvocation.BuildExpression(context);
        var lambda = Expression.Lambda<Func<string>>(expression);
        var compiled = lambda.Compile();
        var result = compiled();

        await Assert.That(result).IsEqualTo("hello");
    }

    [Test]
    public async Task IntegrationTest_MethodFromGetMethodInvocation_WorksCorrectly() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var stringType = registry.GetTypeDefinition<string>();
        var toLowerMethod = (ClrMethod)stringType.Methods.First(m => m.Name == "ToLower" && !m.Parameters.Any());

        var context = new InterpretationContext();
        var instance = Value.Wrap("HELLO");

        // Use the GetMemberAccessor helper from ClrMethod
        var invocation = toLowerMethod.GetMemberAccessor(instance, []);

        var expression = invocation.BuildExpression(context);
        var lambda = Expression.Lambda<Func<string>>(expression);
        var compiled = lambda.Compile();
        var result = compiled();

        await Assert.That(result).IsEqualTo("hello");
    }

    [Test]
    public async Task IntegrationTest_MethodWithComplexTypes_WorksCorrectly() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var listType = registry.GetTypeDefinition<List<int>>();
        var addMethod = (ClrMethod)listType.Methods.First(m =>
            m.Name == "Add" &&
            m.Parameters.Count() == 1);

        var context = new InterpretationContext();
        var list = new List<int>();
        var instance = Value.Wrap(list);
        var argument = Value.Wrap(42);

        var invocation = new ClrMethodInvocationInterpretation(addMethod, instance, [argument]);

        var expression = invocation.BuildExpression(context);
        var lambda = Expression.Lambda<Action>(expression);
        var compiled = lambda.Compile();
        compiled();

        await Assert.That(list.Count).IsEqualTo(1);
        await Assert.That(list[0]).IsEqualTo(42);
    }

    [Test]
    public async Task BuildExpression_WithNoArguments_EmptyArgumentsArray() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var stringType = registry.GetTypeDefinition<string>();
        var toLowerMethod = (ClrMethod)stringType.Methods.First(m => m.Name == "ToLower" && !m.Parameters.Any());
        var instance = Value.Wrap("TEST");
        var invocation = new ClrMethodInvocationInterpretation(toLowerMethod, instance, Array.Empty<Value>());
        var context = new InterpretationContext();

        var expression = invocation.BuildExpression(context);
        var lambda = Expression.Lambda<Func<string>>(expression);
        var compiled = lambda.Compile();
        var result = compiled();

        await Assert.That(result).IsEqualTo("test");
        await Assert.That(invocation.Arguments.Length).IsEqualTo(0);
    }

    [Test]
    public async Task BuildExpression_MethodReturningVoid_WorksCorrectly() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var listType = registry.GetTypeDefinition<List<int>>();
        var clearMethod = (ClrMethod)listType.Methods.First(m => m.Name == "Clear");

        var list = new List<int> { 1, 2, 3 };
        var instance = Value.Wrap(list);
        var invocation = new ClrMethodInvocationInterpretation(clearMethod, instance, []);
        var context = new InterpretationContext();

        var expression = invocation.BuildExpression(context);
        var lambda = Expression.Lambda<Action>(expression);
        var compiled = lambda.Compile();
        compiled();

        await Assert.That(list.Count).IsEqualTo(0);
    }
}