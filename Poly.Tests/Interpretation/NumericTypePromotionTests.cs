using Poly.Interpretation;
using Poly.Interpretation.Operators.Arithmetic;
using System.Linq.Expressions;

namespace Poly.Tests.Interpretation;

public class NumericTypePromotionTests {
    [Test]
    public async Task Add_IntAndDouble_ReturnsDouble() {
        // Arrange
        var context = new InterpretationContext();
        var intValue = Value.Wrap(42);
        var doubleValue = Value.Wrap(3.14);
        var add = new Add(intValue, doubleValue);

        // Act
        var typeDef = add.GetTypeDefinition(context);

        // Assert
        await Assert.That(typeDef.ReflectedType).IsEqualTo(typeof(double));
    }

    [Test]
    public async Task Add_IntAndDouble_EvaluatesCorrectly() {
        // Arrange
        var context = new InterpretationContext();
        var intValue = Value.Wrap(10);
        var doubleValue = Value.Wrap(5.5);
        var add = new Add(intValue, doubleValue);

        // Act
        var expression = add.BuildExpression(context);
        var lambda = Expression.Lambda<Func<double>>(expression);
        var compiled = lambda.Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(15.5);
    }

    [Test]
    public async Task Multiply_FloatAndInt_ReturnsFloat() {
        // Arrange
        var context = new InterpretationContext();
        var floatValue = Value.Wrap(2.5f);
        var intValue = Value.Wrap(4);
        var multiply = new Multiply(floatValue, intValue);

        // Act
        var typeDef = multiply.GetTypeDefinition(context);

        // Assert
        await Assert.That(typeDef.ReflectedType).IsEqualTo(typeof(float));
    }

    [Test]
    public async Task Subtract_LongAndInt_ReturnsLong() {
        // Arrange
        var context = new InterpretationContext();
        var longValue = Value.Wrap(100L);
        var intValue = Value.Wrap(30);
        var subtract = new Subtract(longValue, intValue);

        // Act
        var typeDef = subtract.GetTypeDefinition(context);

        // Assert
        await Assert.That(typeDef.ReflectedType).IsEqualTo(typeof(long));
    }

    [Test]
    public async Task Divide_DecimalAndInt_ReturnsDecimal() {
        // Arrange
        var context = new InterpretationContext();
        var decimalValue = Value.Wrap(100m);
        var intValue = Value.Wrap(3);
        var divide = new Divide(decimalValue, intValue);

        // Act
        var typeDef = divide.GetTypeDefinition(context);

        // Assert
        await Assert.That(typeDef.ReflectedType).IsEqualTo(typeof(decimal));
    }

    [Test]
    public async Task Modulo_DoubleAndFloat_ReturnsDouble() {
        // Arrange
        var context = new InterpretationContext();
        var doubleValue = Value.Wrap(10.5);
        var floatValue = Value.Wrap(3.0f);
        var modulo = new Modulo(doubleValue, floatValue);

        // Act
        var typeDef = modulo.GetTypeDefinition(context);

        // Assert
        await Assert.That(typeDef.ReflectedType).IsEqualTo(typeof(double));
    }

    [Test]
    public async Task Add_TwoInts_ReturnsInt() {
        // Arrange
        var context = new InterpretationContext();
        var intValue1 = Value.Wrap(10);
        var intValue2 = Value.Wrap(20);
        var add = new Add(intValue1, intValue2);

        // Act
        var typeDef = add.GetTypeDefinition(context);

        // Assert
        await Assert.That(typeDef.ReflectedType).IsEqualTo(typeof(int));
    }

    [Test]
    public async Task Add_ByteAndShort_ReturnsInt() {
        // Arrange
        var context = new InterpretationContext();
        var byteValue = Value.Wrap((byte)10);
        var shortValue = Value.Wrap((short)20);
        var add = new Add(byteValue, shortValue);

        // Act
        var typeDef = add.GetTypeDefinition(context);

        // Assert - byte and short promote to int in C#
        await Assert.That(typeDef.ReflectedType).IsEqualTo(typeof(int));
    }

    [Test]
    public async Task Multiply_UIntAndLong_ReturnsLong() {
        // Arrange
        var context = new InterpretationContext();
        var uintValue = Value.Wrap(10u);
        var longValue = Value.Wrap(5L);
        var multiply = new Multiply(uintValue, longValue);

        // Act
        var typeDef = multiply.GetTypeDefinition(context);

        // Assert
        await Assert.That(typeDef.ReflectedType).IsEqualTo(typeof(long));
    }

    [Test]
    public async Task Add_ULongAndInt_ReturnsULong() {
        // Arrange
        var context = new InterpretationContext();
        var ulongValue = Value.Wrap(100UL);
        var intValue = Value.Wrap(50);
        var add = new Add(ulongValue, intValue);

        // Act
        var typeDef = add.GetTypeDefinition(context);

        // Assert
        await Assert.That(typeDef.ReflectedType).IsEqualTo(typeof(ulong));
    }

    [Test]
    public async Task Add_WithParameters_PromotesCorrectly() {
        // Arrange
        var context = new InterpretationContext();
        var intParam = context.AddParameter<int>("x");
        var doubleParam = context.AddParameter<double>("y");
        var add = new Add(intParam, doubleParam);

        // Act
        var expression = add.BuildExpression(context);
        var lambda = Expression.Lambda<Func<int, double, double>>(
            expression,
            intParam.BuildExpression(context),
            doubleParam.BuildExpression(context)
        );
        var compiled = lambda.Compile();

        // Assert
        await Assert.That(compiled(5, 2.5)).IsEqualTo(7.5);
    }
}
