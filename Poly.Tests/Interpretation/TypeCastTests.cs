using System.Linq.Expressions;
using Poly.Interpretation;
using Poly.Interpretation.Expressions;
using Poly.Interpretation.LinqInterpreter;

namespace Poly.Tests.Interpretation;

public class TypeCastTests {
    [Test]
    public async Task TypeCast_IntToDouble_ConvertsCorrectly()
    {
        // Arrange
        var builder = new LinqExecutionPlanBuilder();
        var operand = new Constant(42);
        var doubleType = builder.GetTypeDefinition(typeof(double).FullName!);
        var cast = new TypeCast(operand, doubleType);

        // Act
        var expression = cast.Evaluate(builder);
        var lambda = Expression.Lambda<Func<double>>(expression);
        var compiled = lambda.Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(42.0);
    }

    [Test]
    public async Task TypeCast_DoubleToInt_TruncatesDecimal()
    {
        // Arrange
        var builder = new LinqExecutionPlanBuilder();
        var operand = new Constant(3.14);
        var intType = builder.GetTypeDefinition(typeof(int).FullName!);
        var cast = new TypeCast(operand, intType);

        // Act
        var expression = cast.Evaluate(builder);
        var lambda = Expression.Lambda<Func<int>>(expression);
        var compiled = lambda.Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(3);
    }

    [Test]
    public async Task TypeCast_LongToInt_ConvertsCorrectly()
    {
        // Arrange
        var builder = new LinqExecutionPlanBuilder();
        var operand = new Constant(100L);
        var intType = builder.GetTypeDefinition(typeof(int).FullName!);
        var cast = new TypeCast(operand, intType);

        // Act
        var expression = cast.Evaluate(builder);
        var lambda = Expression.Lambda<Func<int>>(expression);
        var compiled = lambda.Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(100);
    }

    [Test]
    public async Task TypeCast_WithParameter_EvaluatesCorrectly()
    {
        // Arrange
        var builder = new LinqExecutionPlanBuilder();
        var intType = builder.GetTypeDefinition(typeof(int).FullName!);
        var doubleType = builder.GetTypeDefinition(typeof(double).FullName!);
        var param = builder.Parameter("x", intType);
        var paramRef = new NamedReference("x");
        var cast = new TypeCast(paramRef, doubleType);

        // Act
        var expression = cast.Evaluate(builder);
        var lambda = Expression.Lambda<Func<int, double>>(expression, param);
        var compiled = lambda.Compile();

        // Assert
        await Assert.That(compiled(10)).IsEqualTo(10.0);
        await Assert.That(compiled(42)).IsEqualTo(42.0);
    }

    [Test]
    public async Task TypeCast_StringToObject_ConvertsCorrectly()
    {
        // Arrange
        var builder = new LinqExecutionPlanBuilder();
        var stringType = builder.GetTypeDefinition(typeof(string).FullName!);
        var objectType = builder.GetTypeDefinition(typeof(object).FullName!);
        var param = builder.Parameter("str", stringType);
        var paramRef = new NamedReference("str");
        var cast = new TypeCast(paramRef, objectType);

        // Act
        var expression = cast.Evaluate(builder);
        var lambda = Expression.Lambda<Func<string, object>>(expression, param);
        var compiled = lambda.Compile();

        // Assert
        var result = compiled("hello");
        await Assert.That(result).IsEqualTo("hello");
        await Assert.That(result).IsTypeOf<string>();
    }

    [Test]
    public async Task TypeCast_ObjectToString_DowncastsCorrectly()
    {
        // Arrange
        var builder = new LinqExecutionPlanBuilder();
        var objectType = builder.GetTypeDefinition(typeof(object).FullName!);
        var stringType = builder.GetTypeDefinition(typeof(string).FullName!);
        var param = builder.Parameter("obj", objectType);
        var paramRef = new NamedReference("obj");
        var cast = new TypeCast(paramRef, stringType);

        // Act
        var expression = cast.Evaluate(builder);
        var lambda = Expression.Lambda<Func<object, string>>(expression, param);
        var compiled = lambda.Compile();

        // Assert
        var result = compiled("test");
        await Assert.That(result).IsEqualTo("test");
    }

    [Test]
    public async Task TypeCast_NullableToNonNullable_UnwrapsValue()
    {
        // Arrange
        var builder = new LinqExecutionPlanBuilder();
        var nullableIntType = builder.GetTypeDefinition(typeof(int?).FullName!);
        var intType = builder.GetTypeDefinition(typeof(int).FullName!);
        var param = builder.Parameter("nullable", nullableIntType);
        var paramRef = new NamedReference("nullable");
        var cast = new TypeCast(paramRef, intType);

        // Act
        var expression = cast.Evaluate(builder);
        var lambda = Expression.Lambda<Func<int?, int>>(expression, param);
        var compiled = lambda.Compile();

        // Assert
        await Assert.That(compiled(42)).IsEqualTo(42);
    }

    [Test]
    public async Task TypeCast_NonNullableToNullable_WrapsValue()
    {
        // Arrange
        var builder = new LinqExecutionPlanBuilder();
        var intType = builder.GetTypeDefinition(typeof(int).FullName!);
        var nullableIntType = builder.GetTypeDefinition(typeof(int?).FullName!);
        var param = builder.Parameter("value", intType);
        var paramRef = new NamedReference("value");
        var cast = new TypeCast(paramRef, nullableIntType);

        // Act
        var expression = cast.Evaluate(builder);
        var lambda = Expression.Lambda<Func<int, int?>>(expression, param);
        var compiled = lambda.Compile();

        // Assert
        await Assert.That(compiled(42)).IsEqualTo(42);
    }

    [Test]
    public async Task TypeCast_WithNullArguments_ThrowsArgumentNullException()
    {
        // Arrange
        var builder = new LinqExecutionPlanBuilder();
        var intType = builder.GetTypeDefinition(typeof(int).FullName!);

        // Assert
        await Assert.That(() => new TypeCast(null!, intType))
            .Throws<ArgumentNullException>();
        await Assert.That(() => new TypeCast(new Constant(42), null!))
            .Throws<ArgumentNullException>();
    }
}
