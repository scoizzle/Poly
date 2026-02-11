using Poly.Tests.TestHelpers;
using System.Linq.Expressions;

using Poly.Interpretation;
using Expr = System.Linq.Expressions.Expression;
using Poly.Interpretation.AbstractSyntaxTree;
using Poly.Interpretation.AbstractSyntaxTree.Arithmetic;

namespace Poly.Tests.Interpretation;

public class UnaryMinusTests
{
    [Test]
    public async Task UnaryMinus_WithPositiveInteger_ReturnsNegative()
    {
        // Arrange
        var node = new UnaryMinus(Wrap(42));

        // Act
        var expr = node.BuildExpression();
        var compiled = Expr.Lambda<Func<int>>(expr).Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(-42);
    }

    [Test]
    public async Task UnaryMinus_WithNegativeInteger_ReturnsPositive()
    {
        // Arrange
        var node = new UnaryMinus(Wrap(-99));

        // Act
        var expr = node.BuildExpression();
        var compiled = Expr.Lambda<Func<int>>(expr).Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(99);
    }

    [Test]
    public async Task UnaryMinus_WithZero_ReturnsZero()
    {
        // Arrange
        var node = new UnaryMinus(Wrap(0));

        // Act
        var expr = node.BuildExpression();
        var compiled = Expr.Lambda<Func<int>>(expr).Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(0);
    }

    [Test]
    public async Task UnaryMinus_WithDouble_NegatesCorrectly()
    {
        // Arrange
        var node = new UnaryMinus(Wrap(3.14));

        // Act
        var expr = node.BuildExpression();
        var compiled = Expr.Lambda<Func<double>>(expr).Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(-3.14);
    }

    [Test]
    public async Task UnaryMinus_WithParameter_EvaluatesCorrectly()
    {
        // Arrange
        var param = new Parameter("x", TypeReference.To<int>());
        var node = new UnaryMinus(param);

        // Act
        var compiled = node.CompileLambda<Func<int, int>>((param, typeof(int)));

        // Assert
        await Assert.That(compiled(10)).IsEqualTo(-10);
        await Assert.That(compiled(-5)).IsEqualTo(5);
        await Assert.That(compiled(0)).IsEqualTo(0);
    }

    [Test]
    public async Task UnaryMinus_DoubleNegation_ReturnsOriginalValue()
    {
        // Arrange
        var param = new Parameter("x", TypeReference.To<int>());
        var node = new UnaryMinus(new UnaryMinus(param));

        // Act
        var compiled = node.CompileLambda<Func<int, int>>((param, typeof(int)));

        // Assert
        await Assert.That(compiled(42)).IsEqualTo(42);
        await Assert.That(compiled(-7)).IsEqualTo(-7);
    }

    [Test]
    public async Task UnaryMinus_WithArithmeticExpression_EvaluatesCorrectly()
    {
        // Arrange
        var param = new Parameter("x", TypeReference.To<int>());
        var node = new UnaryMinus(new Add(param, Wrap(5)));

        // Act
        var compiled = node.CompileLambda<Func<int, int>>((param, typeof(int)));

        // Assert
        await Assert.That(compiled(10)).IsEqualTo(-15);
        await Assert.That(compiled(-3)).IsEqualTo(-2);
    }

    [Test]
    public async Task UnaryMinus_ToString_ReturnsExpectedFormat()
    {
        // Arrange
        var node = new UnaryMinus(Wrap(42));

        // Act
        var result = node.ToString();

        // Assert
        await Assert.That(result).IsEqualTo("-42");
    }

    [Test]
    public async Task UnaryMinus_WithNullArgument_AllowsNull()
    {
        // Act
        var node = new UnaryMinus(null!);

        // Assert
        await Assert.That(node).IsNotNull();
    }
}
