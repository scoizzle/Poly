using Poly.Interpretation.AbstractSyntaxTree;
using Poly.Interpretation.AbstractSyntaxTree.Arithmetic;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.Interpretation;

/// <summary>
/// Unit tests for arithmetic operation AST nodes (Add, Subtract, Multiply, Divide, Modulo).
/// </summary>
public class ArithmeticNodeTests
{
    // Add Tests
    [Test]
    public async Task Add_TwoIntegers_ReturnsSum()
    {
        // Arrange
        var node = new Add(new Constant(5), new Constant(3));

        // Act
        var expr = node.BuildExpression();
        var lambda = System.Linq.Expressions.Expression.Lambda<Func<int>>(expr);
        var result = lambda.Compile()();

        // Assert
        await Assert.That(result).IsEqualTo(8);
    }

    [Test]
    public async Task Add_WithParameter_ReturnsCorrectSum()
    {
        // Arrange
        var param = new Parameter("x", TypeReference.To<int>());
        var node = new Add(param, new Constant(10));
        var paramExpr = param.GetParameterExpression();

        // Act
        var expr = node.BuildExpression();
        var lambda = System.Linq.Expressions.Expression.Lambda<Func<int, int>>(expr, paramExpr);
        var compiled = lambda.Compile();

        // Assert
        await Assert.That(compiled(5)).IsEqualTo(15);
        await Assert.That(compiled(20)).IsEqualTo(30);
    }

    [Test]
    public async Task Add_TwoDoubles_ReturnsSum()
    {
        // Arrange
        var node = new Add(new Constant(3.5), new Constant(2.5));

        // Act
        var expr = node.BuildExpression();
        var lambda = System.Linq.Expressions.Expression.Lambda<Func<double>>(expr);
        var result = lambda.Compile()();

        // Assert
        await Assert.That(result).IsEqualTo(6.0);
    }

    [Test]
    public async Task Add_IntAndDouble_PromotesToDouble()
    {
        // Arrange
        var node = new Add(new Constant(5), new Constant(3.5));

        // Act
        var expr = node.BuildExpression();
        var lambda = System.Linq.Expressions.Expression.Lambda<Func<double>>(expr);
        var result = lambda.Compile()();

        // Assert
        await Assert.That(result).IsEqualTo(8.5);
    }

    // Subtract Tests
    [Test]
    public async Task Subtract_TwoIntegers_ReturnsDifference()
    {
        // Arrange
        var node = new Subtract(new Constant(10), new Constant(3));

        // Act
        var expr = node.BuildExpression();
        var lambda = System.Linq.Expressions.Expression.Lambda<Func<int>>(expr);
        var result = lambda.Compile()();

        // Assert
        await Assert.That(result).IsEqualTo(7);
    }

    [Test]
    public async Task Subtract_WithParameter_ReturnsCorrectDifference()
    {
        // Arrange
        var param = new Parameter("x", TypeReference.To<int>());
        var node = new Subtract(param, new Constant(5));
        var paramExpr = param.GetParameterExpression();

        // Act
        var expr = node.BuildExpression();
        var lambda = System.Linq.Expressions.Expression.Lambda<Func<int, int>>(expr, paramExpr);
        var compiled = lambda.Compile();

        // Assert
        await Assert.That(compiled(15)).IsEqualTo(10);
        await Assert.That(compiled(8)).IsEqualTo(3);
    }

    [Test]
    public async Task Subtract_ResultingInNegative_ReturnsNegativeNumber()
    {
        // Arrange
        var node = new Subtract(new Constant(5), new Constant(10));

        // Act
        var expr = node.BuildExpression();
        var lambda = System.Linq.Expressions.Expression.Lambda<Func<int>>(expr);
        var result = lambda.Compile()();

        // Assert
        await Assert.That(result).IsEqualTo(-5);
    }

    // Multiply Tests
    [Test]
    public async Task Multiply_TwoIntegers_ReturnsProduct()
    {
        // Arrange
        var node = new Multiply(new Constant(6), new Constant(7));

        // Act
        var expr = node.BuildExpression();
        var lambda = System.Linq.Expressions.Expression.Lambda<Func<int>>(expr);
        var result = lambda.Compile()();

        // Assert
        await Assert.That(result).IsEqualTo(42);
    }

    [Test]
    public async Task Multiply_WithParameter_ReturnsCorrectProduct()
    {
        // Arrange
        var param = new Parameter("x", TypeReference.To<int>());
        var node = new Multiply(param, new Constant(3));
        var paramExpr = param.GetParameterExpression();

        // Act
        var expr = node.BuildExpression();
        var lambda = System.Linq.Expressions.Expression.Lambda<Func<int, int>>(expr, paramExpr);
        var compiled = lambda.Compile();

        // Assert
        await Assert.That(compiled(4)).IsEqualTo(12);
        await Assert.That(compiled(10)).IsEqualTo(30);
    }

    [Test]
    public async Task Multiply_IntAndDouble_PromotesToDouble()
    {
        // Arrange
        var node = new Multiply(new Constant(4), new Constant(2.5));

        // Act
        var expr = node.BuildExpression();
        var lambda = System.Linq.Expressions.Expression.Lambda<Func<double>>(expr);
        var result = lambda.Compile()();

        // Assert
        await Assert.That(result).IsEqualTo(10.0);
    }

    // Divide Tests
    [Test]
    public async Task Divide_TwoIntegers_ReturnsQuotient()
    {
        // Arrange
        var node = new Divide(new Constant(20), new Constant(4));

        // Act
        var expr = node.BuildExpression();
        var lambda = System.Linq.Expressions.Expression.Lambda<Func<int>>(expr);
        var result = lambda.Compile()();

        // Assert
        await Assert.That(result).IsEqualTo(5);
    }

    [Test]
    public async Task Divide_IntegerDivision_TruncatesResult()
    {
        // Arrange
        var node = new Divide(new Constant(7), new Constant(2));

        // Act
        var expr = node.BuildExpression();
        var lambda = System.Linq.Expressions.Expression.Lambda<Func<int>>(expr);
        var result = lambda.Compile()();

        // Assert
        await Assert.That(result).IsEqualTo(3);
    }

    [Test]
    public async Task Divide_TwoDoubles_ReturnsDecimalQuotient()
    {
        // Arrange
        var node = new Divide(new Constant(7.0), new Constant(2.0));

        // Act
        var expr = node.BuildExpression();
        var lambda = System.Linq.Expressions.Expression.Lambda<Func<double>>(expr);
        var result = lambda.Compile()();

        // Assert
        await Assert.That(result).IsEqualTo(3.5);
    }

    [Test]
    public async Task Divide_WithParameter_ReturnsCorrectQuotient()
    {
        // Arrange
        var param = new Parameter("x", TypeReference.To<int>());
        var node = new Divide(param, new Constant(2));
        var paramExpr = param.GetParameterExpression();

        // Act
        var expr = node.BuildExpression();
        var lambda = System.Linq.Expressions.Expression.Lambda<Func<int, int>>(expr, paramExpr);
        var compiled = lambda.Compile();

        // Assert
        await Assert.That(compiled(10)).IsEqualTo(5);
        await Assert.That(compiled(21)).IsEqualTo(10);
    }

    // Modulo Tests
    [Test]
    public async Task Modulo_TwoIntegers_ReturnsRemainder()
    {
        // Arrange
        var node = new Modulo(new Constant(10), new Constant(3));

        // Act
        var expr = node.BuildExpression();
        var lambda = System.Linq.Expressions.Expression.Lambda<Func<int>>(expr);
        var result = lambda.Compile()();

        // Assert
        await Assert.That(result).IsEqualTo(1);
    }

    [Test]
    public async Task Modulo_ExactDivision_ReturnsZero()
    {
        // Arrange
        var node = new Modulo(new Constant(15), new Constant(5));

        // Act
        var expr = node.BuildExpression();
        var lambda = System.Linq.Expressions.Expression.Lambda<Func<int>>(expr);
        var result = lambda.Compile()();

        // Assert
        await Assert.That(result).IsEqualTo(0);
    }

    [Test]
    public async Task Modulo_WithParameter_ReturnsCorrectRemainder()
    {
        // Arrange
        var param = new Parameter("x", TypeReference.To<int>());
        var node = new Modulo(param, new Constant(7));
        var paramExpr = param.GetParameterExpression();

        // Act
        var expr = node.BuildExpression();
        var lambda = System.Linq.Expressions.Expression.Lambda<Func<int, int>>(expr, paramExpr);
        var compiled = lambda.Compile();

        // Assert
        await Assert.That(compiled(15)).IsEqualTo(1);
        await Assert.That(compiled(20)).IsEqualTo(6);
    }

    // Nested Operations Tests
    [Test]
    public async Task NestedOperations_AddAndMultiply_EvaluatesCorrectly()
    {
        // Arrange - (5 + 3) * 2 = 16
        var add = new Add(new Constant(5), new Constant(3));
        var node = new Multiply(add, new Constant(2));

        // Act
        var expr = node.BuildExpression();
        var lambda = System.Linq.Expressions.Expression.Lambda<Func<int>>(expr);
        var result = lambda.Compile()();

        // Assert
        await Assert.That(result).IsEqualTo(16);
    }

    [Test]
    public async Task NestedOperations_MultiplyAndAdd_EvaluatesCorrectly()
    {
        // Arrange - 2 * 3 + 4 = 10
        var multiply = new Multiply(new Constant(2), new Constant(3));
        var node = new Add(multiply, new Constant(4));

        // Act
        var expr = node.BuildExpression();
        var lambda = System.Linq.Expressions.Expression.Lambda<Func<int>>(expr);
        var result = lambda.Compile()();

        // Assert
        await Assert.That(result).IsEqualTo(10);
    }

    [Test]
    public async Task ComplexExpression_MultipleOperations_EvaluatesCorrectly()
    {
        // Arrange - ((10 + 5) * 2) - 3 = 27
        var add = new Add(new Constant(10), new Constant(5));
        var multiply = new Multiply(add, new Constant(2));
        var node = new Subtract(multiply, new Constant(3));

        // Act
        var expr = node.BuildExpression();
        var lambda = System.Linq.Expressions.Expression.Lambda<Func<int>>(expr);
        var result = lambda.Compile()();

        // Assert
        await Assert.That(result).IsEqualTo(27);
    }
}
