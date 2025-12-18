using System.Linq.Expressions;

using Poly.Interpretation;
using Poly.Interpretation.Operators.Arithmetic;

namespace Poly.Tests.Interpretation;

public class UnaryMinusTests {
    [Test]
    public async Task UnaryMinus_WithPositiveInteger_ReturnsNegative() {
        // Arrange
        var context = new InterpretationContext();
        var operand = Value.Wrap(42);
        var unaryMinus = new UnaryMinus(operand);

        // Act
        var expression = unaryMinus.BuildExpression(context);
        var lambda = Expression.Lambda<Func<int>>(expression);
        var compiled = lambda.Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(-42);
    }

    [Test]
    public async Task UnaryMinus_WithNegativeInteger_ReturnsPositive() {
        // Arrange
        var context = new InterpretationContext();
        var operand = Value.Wrap(-99);
        var unaryMinus = new UnaryMinus(operand);

        // Act
        var expression = unaryMinus.BuildExpression(context);
        var lambda = Expression.Lambda<Func<int>>(expression);
        var compiled = lambda.Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(99);
    }

    [Test]
    public async Task UnaryMinus_WithZero_ReturnsZero() {
        // Arrange
        var context = new InterpretationContext();
        var operand = Value.Wrap(0);
        var unaryMinus = new UnaryMinus(operand);

        // Act
        var expression = unaryMinus.BuildExpression(context);
        var lambda = Expression.Lambda<Func<int>>(expression);
        var compiled = lambda.Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(0);
    }

    [Test]
    public async Task UnaryMinus_WithDouble_NegatesCorrectly() {
        // Arrange
        var context = new InterpretationContext();
        var operand = Value.Wrap(3.14);
        var unaryMinus = new UnaryMinus(operand);

        // Act
        var expression = unaryMinus.BuildExpression(context);
        var lambda = Expression.Lambda<Func<double>>(expression);
        var compiled = lambda.Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(-3.14);
    }

    [Test]
    public async Task UnaryMinus_WithParameter_EvaluatesCorrectly() {
        // Arrange
        var context = new InterpretationContext();
        var param = context.AddParameter<int>("x");
        var unaryMinus = new UnaryMinus(param);

        // Act
        var expression = unaryMinus.BuildExpression(context);
        var lambda = Expression.Lambda<Func<int, int>>(expression, param.BuildExpression(context));
        var compiled = lambda.Compile();

        // Assert
        await Assert.That(compiled(10)).IsEqualTo(-10);
        await Assert.That(compiled(-5)).IsEqualTo(5);
        await Assert.That(compiled(0)).IsEqualTo(0);
    }

    [Test]
    public async Task UnaryMinus_DoubleNegation_ReturnsOriginalValue() {
        // Arrange
        var context = new InterpretationContext();
        var param = context.AddParameter<int>("x");
        var innerNegate = new UnaryMinus(param);
        var outerNegate = new UnaryMinus(innerNegate);

        // Act
        var expression = outerNegate.BuildExpression(context);
        var lambda = Expression.Lambda<Func<int, int>>(expression, param.BuildExpression(context));
        var compiled = lambda.Compile();

        // Assert
        await Assert.That(compiled(42)).IsEqualTo(42);
        await Assert.That(compiled(-7)).IsEqualTo(-7);
    }

    [Test]
    public async Task UnaryMinus_WithArithmeticExpression_EvaluatesCorrectly() {
        // Arrange
        var context = new InterpretationContext();
        var param = context.AddParameter<int>("x");

        // -(x + 5)
        var add = new Add(param, Value.Wrap(5));
        var negate = new UnaryMinus(add);

        // Act
        var expression = negate.BuildExpression(context);
        var lambda = Expression.Lambda<Func<int, int>>(expression, param.BuildExpression(context));
        var compiled = lambda.Compile();

        // Assert
        await Assert.That(compiled(10)).IsEqualTo(-15);
        await Assert.That(compiled(-3)).IsEqualTo(-2);
    }

    [Test]
    public async Task UnaryMinus_GetTypeDefinition_ReturnsOperandType() {
        // Arrange
        var context = new InterpretationContext();
        var operand = Value.Wrap(42);
        var unaryMinus = new UnaryMinus(operand);

        // Act
        var typeDef = unaryMinus.GetTypeDefinition(context);

        // Assert
        await Assert.That(typeDef).IsNotNull();
        await Assert.That(typeDef.ReflectedType).IsEqualTo(typeof(int));
    }

    [Test]
    public async Task UnaryMinus_ToString_ReturnsExpectedFormat() {
        // Arrange
        var operand = Value.Wrap(42);
        var unaryMinus = new UnaryMinus(operand);

        // Act
        var result = unaryMinus.ToString();

        // Assert
        await Assert.That(result).IsEqualTo("-42");
    }

    [Test]
    public async Task UnaryMinus_WithNullArgument_ThrowsArgumentNullException() {
        // Assert
        await Assert.That(() => new UnaryMinus(null!))
            .Throws<ArgumentNullException>();
    }
}