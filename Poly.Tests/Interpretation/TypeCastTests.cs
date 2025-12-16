using Poly.Interpretation;
using Poly.Interpretation.Operators;
using System.Linq.Expressions;

namespace Poly.Tests.Interpretation;

public class TypeCastTests {
    [Test]
    public async Task TypeCast_IntToDouble_ConvertsCorrectly() {
        // Arrange
        var context = new InterpretationContext();
        var operand = Value.Wrap(42);
        var doubleType = context.GetTypeDefinition<double>()!;
        var cast = new TypeCast(operand, doubleType);

        // Act
        var expression = cast.BuildExpression(context);
        var lambda = Expression.Lambda<Func<double>>(expression);
        var compiled = lambda.Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(42.0);
    }

    [Test]
    public async Task TypeCast_DoubleToInt_TruncatesDecimal() {
        // Arrange
        var context = new InterpretationContext();
        var operand = Value.Wrap(3.14);
        var intType = context.GetTypeDefinition<int>()!;
        var cast = new TypeCast(operand, intType);

        // Act
        var expression = cast.BuildExpression(context);
        var lambda = Expression.Lambda<Func<int>>(expression);
        var compiled = lambda.Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(3);
    }

    [Test]
    public async Task TypeCast_LongToInt_ConvertsCorrectly() {
        // Arrange
        var context = new InterpretationContext();
        var operand = Value.Wrap(100L);
        var intType = context.GetTypeDefinition<int>()!;
        var cast = new TypeCast(operand, intType);

        // Act
        var expression = cast.BuildExpression(context);
        var lambda = Expression.Lambda<Func<int>>(expression);
        var compiled = lambda.Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(100);
    }

    [Test]
    public async Task TypeCast_WithParameter_EvaluatesCorrectly() {
        // Arrange
        var context = new InterpretationContext();
        var param = context.AddParameter<int>("x");
        var doubleType = context.GetTypeDefinition<double>()!;
        var cast = new TypeCast(param, doubleType);

        // Act
        var expression = cast.BuildExpression(context);
        var lambda = Expression.Lambda<Func<int, double>>(expression, param.BuildExpression(context));
        var compiled = lambda.Compile();

        // Assert
        await Assert.That(compiled(10)).IsEqualTo(10.0);
        await Assert.That(compiled(42)).IsEqualTo(42.0);
    }

    [Test]
    public async Task TypeCast_StringToObject_ConvertsCorrectly() {
        // Arrange
        var context = new InterpretationContext();
        var param = context.AddParameter<string>("str");
        var objectType = context.GetTypeDefinition<object>()!;
        var cast = new TypeCast(param, objectType);

        // Act
        var expression = cast.BuildExpression(context);
        var lambda = Expression.Lambda<Func<string, object>>(expression, param.BuildExpression(context));
        var compiled = lambda.Compile();

        // Assert
        var result = compiled("hello");
        await Assert.That(result).IsEqualTo("hello");
        await Assert.That(result).IsTypeOf<string>();
    }

    [Test]
    public async Task TypeCast_ObjectToString_DowncastsCorrectly() {
        // Arrange
        var context = new InterpretationContext();
        var param = context.AddParameter<object>("obj");
        var stringType = context.GetTypeDefinition<string>()!;
        var cast = new TypeCast(param, stringType);

        // Act
        var expression = cast.BuildExpression(context);
        var lambda = Expression.Lambda<Func<object, string>>(expression, param.BuildExpression(context));
        var compiled = lambda.Compile();

        // Assert
        var result = compiled("test");
        await Assert.That(result).IsEqualTo("test");
    }

    [Test]
    public async Task TypeCast_NullableToNonNullable_UnwrapsValue() {
        // Arrange
        var context = new InterpretationContext();
        var param = context.AddParameter<int?>("nullable");
        var intType = context.GetTypeDefinition<int>()!;
        var cast = new TypeCast(param, intType);

        // Act
        var expression = cast.BuildExpression(context);
        var lambda = Expression.Lambda<Func<int?, int>>(expression, param.BuildExpression(context));
        var compiled = lambda.Compile();

        // Assert
        await Assert.That(compiled(42)).IsEqualTo(42);
    }

    [Test]
    public async Task TypeCast_NonNullableToNullable_WrapsValue() {
        // Arrange
        var context = new InterpretationContext();
        var param = context.AddParameter<int>("value");
        var nullableIntType = context.GetTypeDefinition<int?>()!;
        var cast = new TypeCast(param, nullableIntType);

        // Act
        var expression = cast.BuildExpression(context);
        var lambda = Expression.Lambda<Func<int, int?>>(expression, param.BuildExpression(context));
        var compiled = lambda.Compile();

        // Assert
        await Assert.That(compiled(42)).IsEqualTo(42);
    }

    [Test]
    public async Task TypeCast_GetTypeDefinition_ReturnsTargetType() {
        // Arrange
        var context = new InterpretationContext();
        var operand = Value.Wrap(42);
        var doubleType = context.GetTypeDefinition<double>()!;
        var cast = new TypeCast(operand, doubleType);

        // Act
        var typeDef = cast.GetTypeDefinition(context);

        // Assert
        await Assert.That(typeDef).IsNotNull();
        await Assert.That(typeDef.ReflectedType).IsEqualTo(typeof(double));
    }

    [Test]
    public async Task TypeCast_ToString_ReturnsExpectedFormat() {
        // Arrange
        var context = new InterpretationContext();
        var operand = Value.Wrap(42);
        var doubleType = context.GetTypeDefinition<double>()!;
        var cast = new TypeCast(operand, doubleType);

        // Act
        var result = cast.ToString();

        // Assert
        await Assert.That(result).Contains("Double");
        await Assert.That(result).Contains("42");
    }

    [Test]
    public async Task TypeCast_WithNullArguments_ThrowsArgumentNullException() {
        // Arrange
        var context = new InterpretationContext();
        var doubleType = context.GetTypeDefinition<double>()!;

        // Assert
        await Assert.That(() => new TypeCast(null!, doubleType))
            .Throws<ArgumentNullException>();
        await Assert.That(() => new TypeCast(Value.Wrap(42), null!))
            .Throws<ArgumentNullException>();
    }
}
