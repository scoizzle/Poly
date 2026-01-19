using Poly.Tests.TestHelpers;
using System.Linq.Expressions;

using Poly.Interpretation;
using Expr = System.Linq.Expressions.Expression;
using Poly.Interpretation.AbstractSyntaxTree;

namespace Poly.Tests.Interpretation;

public class TypeCastTests {
    [Test]
    public async Task TypeCast_IntToDouble_ConvertsCorrectly()
    {
        // Arrange
        var context = new InterpretationContext();
        var operand = Wrap(42);
        var doubleType = context.GetTypeDefinition<double>()!;
        var cast = new TypeCast(operand, doubleType);

        // Act
        var expression = cast.BuildExpression(context);
        var lambda = Expr.Lambda<Func<double>>(expression);
        var compiled = lambda.Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(42.0);
    }

    [Test]
    public async Task TypeCast_DoubleToInt_TruncatesDecimal()
    {
        // Arrange
        var context = new InterpretationContext();
        var operand = Wrap(3.14);
        var intType = context.GetTypeDefinition<int>()!;
        var cast = new TypeCast(operand, intType);

        // Act
        var expression = cast.BuildExpression(context);
        var lambda = Expr.Lambda<Func<int>>(expression);
        var compiled = lambda.Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(3);
    }

    [Test]
    public async Task TypeCast_LongToInt_ConvertsCorrectly()
    {
        // Arrange
        var context = new InterpretationContext();
        var operand = Wrap(100L);
        var intType = context.GetTypeDefinition<int>()!;
        var cast = new TypeCast(operand, intType);

        // Act
        var expression = cast.BuildExpression(context);
        var lambda = Expr.Lambda<Func<int>>(expression);
        var compiled = lambda.Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(100);
    }

    [Test]
    public async Task TypeCast_WithParameter_EvaluatesCorrectly()
    {
        // Arrange
        var context = new InterpretationContext();
        var param = context.AddParameter<int>("x");
        var doubleType = context.GetTypeDefinition<double>()!;
        var cast = new TypeCast(param, doubleType);

        // Act
        var expression = cast.BuildExpression(context);
        var lambda = Expr.Lambda<Func<int, double>>(expression, param.GetParameterExpression(context));
        var compiled = lambda.Compile();

        // Assert
        await Assert.That(compiled(10)).IsEqualTo(10.0);
        await Assert.That(compiled(42)).IsEqualTo(42.0);
    }

    [Test]
    public async Task TypeCast_StringToObject_ConvertsCorrectly()
    {
        // Arrange
        var context = new InterpretationContext();
        var param = context.AddParameter<string>("str");
        var objectType = context.GetTypeDefinition<object>()!;
        var cast = new TypeCast(param, objectType);

        // Act
        var expression = cast.BuildExpression(context);
        var lambda = Expr.Lambda<Func<string, object>>(expression, param.GetParameterExpression(context));
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
        var context = new InterpretationContext();
        var param = context.AddParameter<object>("obj");
        var stringType = context.GetTypeDefinition<string>()!;
        var cast = new TypeCast(param, stringType);

        // Act
        var expression = cast.BuildExpression(context);
        var lambda = Expr.Lambda<Func<object, string>>(expression, param.GetParameterExpression(context));
        var compiled = lambda.Compile();

        // Assert
        var result = compiled("test");
        await Assert.That(result).IsEqualTo("test");
    }

    [Test]
    public async Task TypeCast_NullableToNonNullable_UnwrapsValue()
    {
        // Arrange
        var context = new InterpretationContext();
        var param = context.AddParameter<int?>("nullable");
        var intType = context.GetTypeDefinition<int>()!;
        var cast = new TypeCast(param, intType);

        // Act
        var expression = cast.BuildExpression(context);
        var lambda = Expr.Lambda<Func<int?, int>>(expression, param.GetParameterExpression(context));
        var compiled = lambda.Compile();

        // Assert
        await Assert.That(compiled(42)).IsEqualTo(42);
    }

    [Test]
    public async Task TypeCast_NonNullableToNullable_WrapsValue()
    {
        // Arrange
        var context = new InterpretationContext();
        var param = context.AddParameter<int>("value");
        var nullableIntType = context.GetTypeDefinition<int?>()!;
        var cast = new TypeCast(param, nullableIntType);

        // Act
        var expression = cast.BuildExpression(context);
        var lambda = Expr.Lambda<Func<int, int?>>(expression, param.GetParameterExpression(context));
        var compiled = lambda.Compile();

        // Assert
        await Assert.That(compiled(42)).IsEqualTo(42);
    }

    [Test]
    public async Task TypeCast_GetTypeDefinition_ReturnsTargetType()
    {
        // Arrange
        var context = new InterpretationContext();
        var operand = Wrap(42);
        var doubleType = context.GetTypeDefinition<double>()!;
        var cast = new TypeCast(operand, doubleType);

        // Act
        var typeDef = cast.GetResolvedType(context);

        // Assert
        await Assert.That(typeDef).IsNotNull();
        await Assert.That(typeDef.ReflectedType).IsEqualTo(typeof(double));
    }

    [Test]
    public async Task TypeCast_ToString_ReturnsExpectedFormat()
    {
        // Arrange
        var context = new InterpretationContext();
        var operand = Wrap(42);
        var doubleType = context.GetTypeDefinition<double>()!;
        var cast = new TypeCast(operand, doubleType);

        // Act
        var result = cast.ToString();

        // Assert
        await Assert.That(result).Contains("Double");
        await Assert.That(result).Contains("42");
    }

    [Test]
    public async Task TypeCast_WithNullArguments_AllowsNulls()
    {
        // Arrange
        var context = new InterpretationContext();
        var doubleType = context.GetTypeDefinition<double>()!;

        // Act
        var c1 = new TypeCast(null!, doubleType);
        var c2 = new TypeCast(Wrap(42), null!);

        // Assert
        await Assert.That(c1).IsNotNull();
        await Assert.That(c2).IsNotNull();
    }
}