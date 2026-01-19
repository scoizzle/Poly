using Poly.Tests.TestHelpers;
using System.Linq.Expressions;

using Poly.Interpretation;
using Expr = System.Linq.Expressions.Expression;
using Poly.Interpretation.AbstractSyntaxTree.Arithmetic;

namespace Poly.Tests.Interpretation;

public class NumericTypePromotionTests {
    [Test]
    public async Task Add_IntAndDouble_ReturnsDouble()
    {
        // Arrange
        var context = new InterpretationContext();
        var intValue = Wrap(42);
        var doubleValue = Wrap(3.14);
        var add = new Add(intValue, doubleValue);

        // Act
        var typeDef = add.GetResolvedType(context);

        // Assert
        await Assert.That(typeDef.ReflectedType).IsEqualTo(typeof(double));
    }

    [Test]
    public async Task Add_IntAndDouble_EvaluatesCorrectly()
    {
        // Arrange
        var context = new InterpretationContext();
        var intValue = Wrap(10);
        var doubleValue = Wrap(5.5);
        var add = new Add(intValue, doubleValue);

        // Act
        var expression = add.BuildExpression(context);
        var lambda = Expr.Lambda<Func<double>>(expression);
        var compiled = lambda.Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(15.5);
    }

    [Test]
    public async Task Multiply_FloatAndInt_ReturnsFloat()
    {
        // Arrange
        var context = new InterpretationContext();
        var floatValue = Wrap(2.5f);
        var intValue = Wrap(4);
        var multiply = new Multiply(floatValue, intValue);

        // Act
        var typeDef = multiply.GetResolvedType(context);

        // Assert
        await Assert.That(typeDef.ReflectedType).IsEqualTo(typeof(float));
    }

    [Test]
    public async Task Subtract_LongAndInt_ReturnsLong()
    {
        // Arrange
        var context = new InterpretationContext();
        var longValue = Wrap(100L);
        var intValue = Wrap(30);
        var subtract = new Subtract(longValue, intValue);

        // Act
        var typeDef = subtract.GetResolvedType(context);

        // Assert
        await Assert.That(typeDef.ReflectedType).IsEqualTo(typeof(long));
    }

    [Test]
    public async Task Divide_DecimalAndInt_ReturnsDecimal()
    {
        // Arrange
        var context = new InterpretationContext();
        var decimalValue = Wrap(100m);
        var intValue = Wrap(3);
        var divide = new Divide(decimalValue, intValue);

        // Act
        var typeDef = divide.GetResolvedType(context);

        // Assert
        await Assert.That(typeDef.ReflectedType).IsEqualTo(typeof(decimal));
    }

    [Test]
    public async Task Modulo_DoubleAndFloat_ReturnsDouble()
    {
        // Arrange
        var context = new InterpretationContext();
        var doubleValue = Wrap(10.5);
        var floatValue = Wrap(3.0f);
        var modulo = new Modulo(doubleValue, floatValue);

        // Act
        var typeDef = modulo.GetResolvedType(context);

        // Assert
        await Assert.That(typeDef.ReflectedType).IsEqualTo(typeof(double));
    }

    [Test]
    public async Task Add_TwoInts_ReturnsInt()
    {
        // Arrange
        var context = new InterpretationContext();
        var intValue1 = Wrap(10);
        var intValue2 = Wrap(20);
        var add = new Add(intValue1, intValue2);

        // Act
        var typeDef = add.GetResolvedType(context);

        // Assert
        await Assert.That(typeDef.ReflectedType).IsEqualTo(typeof(int));
    }

    [Test]
    public async Task Add_ByteAndShort_ReturnsInt()
    {
        // Arrange
        var context = new InterpretationContext();
        var byteValue = Wrap((byte)10);
        var shortValue = Wrap((short)20);
        var add = new Add(byteValue, shortValue);

        // Act
        var typeDef = add.GetResolvedType(context);

        // Assert - byte and short promote to int in C#
        await Assert.That(typeDef.ReflectedType).IsEqualTo(typeof(int));
    }

    [Test]
    public async Task Multiply_UIntAndLong_ReturnsLong()
    {
        // Arrange
        var context = new InterpretationContext();
        var uintValue = Wrap(10u);
        var longValue = Wrap(5L);
        var multiply = new Multiply(uintValue, longValue);

        // Act
        var typeDef = multiply.GetResolvedType(context);

        // Assert
        await Assert.That(typeDef.ReflectedType).IsEqualTo(typeof(long));
    }

    [Test]
    public async Task Add_ULongAndInt_ReturnsULong()
    {
        // Arrange
        var context = new InterpretationContext();
        var ulongValue = Wrap(100UL);
        var intValue = Wrap(50);
        var add = new Add(ulongValue, intValue);

        // Act
        var typeDef = add.GetResolvedType(context);

        // Assert
        await Assert.That(typeDef.ReflectedType).IsEqualTo(typeof(ulong));
    }

    [Test]
    public async Task Add_WithParameters_PromotesCorrectly()
    {
        // Arrange
        var context = new InterpretationContext();
        var intParam = context.AddParameter<int>("x");
        var doubleParam = context.AddParameter<double>("y");
        var add = new Add(intParam, doubleParam);

        // Act
        var expression = add.BuildExpression(context);
        var lambda = Expr.Lambda<Func<int, double, double>>(
            expression,
            intParam.GetParameterExpression(context),
            doubleParam.GetParameterExpression(context)
        );
        var compiled = lambda.Compile();

        // Assert
        await Assert.That(compiled(5, 2.5)).IsEqualTo(7.5);
    }
}