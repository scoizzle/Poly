using System.Linq.Expressions;

using Poly.Interpretation;
using Poly.Interpretation.Operators.Arithmetic;

namespace Poly.Tests.Interpretation;

public class ModuloTests {
    [Test]
    public async Task Modulo_WithIntegers_ReturnsRemainder() {
        // Arrange
        var context = new InterpretationContext();
        var leftValue = Value.Wrap(10);
        var rightValue = Value.Wrap(3);
        var modulo = new Modulo(leftValue, rightValue);

        // Act
        var expression = modulo.BuildExpression(context);
        var lambda = Expression.Lambda<Func<int>>(expression);
        var compiled = lambda.Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(1);
    }

    [Test]
    public async Task Modulo_WithExactDivision_ReturnsZero() {
        // Arrange
        var context = new InterpretationContext();
        var leftValue = Value.Wrap(15);
        var rightValue = Value.Wrap(5);
        var modulo = new Modulo(leftValue, rightValue);

        // Act
        var expression = modulo.BuildExpression(context);
        var lambda = Expression.Lambda<Func<int>>(expression);
        var compiled = lambda.Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(0);
    }

    [Test]
    public async Task Modulo_WithDoubles_ReturnsRemainder() {
        // Arrange
        var context = new InterpretationContext();
        var leftValue = Value.Wrap(10.5);
        var rightValue = Value.Wrap(3.0);
        var modulo = new Modulo(leftValue, rightValue);

        // Act
        var expression = modulo.BuildExpression(context);
        var lambda = Expression.Lambda<Func<double>>(expression);
        var compiled = lambda.Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(1.5);
    }

    [Test]
    public async Task Modulo_WithParameters_EvaluatesCorrectly() {
        // Arrange
        var context = new InterpretationContext();
        var param1 = context.AddParameter<int>("a");
        var param2 = context.AddParameter<int>("b");
        var modulo = new Modulo(param1, param2);

        // Act
        var expression = modulo.BuildExpression(context);
        var lambda = Expression.Lambda<Func<int, int, int>>(
            expression,
            param1.BuildExpression(context),
            param2.BuildExpression(context)
        );
        var compiled = lambda.Compile();

        // Assert
        await Assert.That(compiled(17, 5)).IsEqualTo(2);
        await Assert.That(compiled(100, 7)).IsEqualTo(2);
        await Assert.That(compiled(8, 4)).IsEqualTo(0);
    }

    [Test]
    public async Task Modulo_WithNegativeNumbers_HandlesCorrectly() {
        // Arrange
        var context = new InterpretationContext();
        var leftValue = Value.Wrap(-10);
        var rightValue = Value.Wrap(3);
        var modulo = new Modulo(leftValue, rightValue);

        // Act
        var expression = modulo.BuildExpression(context);
        var lambda = Expression.Lambda<Func<int>>(expression);
        var compiled = lambda.Compile();
        var result = compiled();

        // Assert: C# modulo preserves sign of dividend
        await Assert.That(result).IsEqualTo(-1);
    }

    [Test]
    public async Task Modulo_GetTypeDefinition_ReturnsLeftHandType() {
        // Arrange
        var context = new InterpretationContext();
        var leftValue = Value.Wrap(10);
        var rightValue = Value.Wrap(3);
        var modulo = new Modulo(leftValue, rightValue);

        // Act
        var typeDef = modulo.GetTypeDefinition(context);

        // Assert
        await Assert.That(typeDef).IsNotNull();
        await Assert.That(typeDef.ReflectedType).IsEqualTo(typeof(int));
    }

    [Test]
    public async Task Modulo_ToString_ReturnsExpectedFormat() {
        // Arrange
        var leftValue = Value.Wrap(10);
        var rightValue = Value.Wrap(3);
        var modulo = new Modulo(leftValue, rightValue);

        // Act
        var result = modulo.ToString();

        // Assert
        await Assert.That(result).IsEqualTo("(10 % 3)");
    }

    [Test]
    public async Task Modulo_WithNullArguments_ThrowsArgumentNullException() {
        // Assert
        await Assert.That(() => new Modulo(null!, Value.Wrap(3)))
            .Throws<ArgumentNullException>();
        await Assert.That(() => new Modulo(Value.Wrap(10), null!))
            .Throws<ArgumentNullException>();
    }
}