using Poly.Interpretation;
using Poly.Interpretation.AbstractSyntaxTree;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.Interpretation;

/// <summary>
/// Unit tests for Constant AST nodes and their LINQ expression compilation.
/// </summary>
public class ConstantNodeTests
{
    [Test]
    public async Task Constant_IntegerValue_CompilesAndExecutes()
    {
        // Arrange
        var node = new Constant(42);

        // Act
        var expr = node.BuildExpression();
        var lambda = System.Linq.Expressions.Expression.Lambda<Func<int>>(expr);
        var result = lambda.Compile()();

        // Assert
        await Assert.That(result).IsEqualTo(42);
    }

    [Test]
    public async Task Constant_StringValue_CompilesAndExecutes()
    {
        // Arrange
        var node = new Constant("hello");

        // Act
        var expr = node.BuildExpression();
        var lambda = System.Linq.Expressions.Expression.Lambda<Func<string>>(expr);
        var result = lambda.Compile()();

        // Assert
        await Assert.That(result).IsEqualTo("hello");
    }

    [Test]
    public async Task Constant_DoubleValue_CompilesAndExecutes()
    {
        // Arrange
        var node = new Constant(3.14);

        // Act
        var expr = node.BuildExpression();
        var lambda = System.Linq.Expressions.Expression.Lambda<Func<double>>(expr);
        var result = lambda.Compile()();

        // Assert
        await Assert.That(result).IsEqualTo(3.14);
    }

    [Test]
    public async Task Constant_BooleanTrue_CompilesAndExecutes()
    {
        // Arrange
        var node = new Constant(true);

        // Act
        var expr = node.BuildExpression();
        var lambda = System.Linq.Expressions.Expression.Lambda<Func<bool>>(expr);
        var result = lambda.Compile()();

        // Assert
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task Constant_BooleanFalse_CompilesAndExecutes()
    {
        // Arrange
        var node = new Constant(false);

        // Act
        var expr = node.BuildExpression();
        var lambda = System.Linq.Expressions.Expression.Lambda<Func<bool>>(expr);
        var result = lambda.Compile()();

        // Assert
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task Constant_NullValue_CompilesAndExecutes()
    {
        // Arrange
        var node = new Constant(null);

        // Act
        var expr = node.BuildExpression();
        var lambda = System.Linq.Expressions.Expression.Lambda<Func<object?>>(expr);
        var result = lambda.Compile()();

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task Constant_DecimalValue_CompilesAndExecutes()
    {
        // Arrange
        var node = new Constant(99.99m);

        // Act
        var expr = node.BuildExpression();
        var lambda = System.Linq.Expressions.Expression.Lambda<Func<decimal>>(expr);
        var result = lambda.Compile()();

        // Assert
        await Assert.That(result).IsEqualTo(99.99m);
    }

    [Test]
    public async Task Constant_DateTime_CompilesAndExecutes()
    {
        // Arrange
        var expected = new DateTime(2024, 1, 15, 10, 30, 0);
        var node = new Constant(expected);

        // Act
        var expr = node.BuildExpression();
        var lambda = System.Linq.Expressions.Expression.Lambda<Func<DateTime>>(expr);
        var result = lambda.Compile()();

        // Assert
        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    public async Task Constant_ComplexObject_CompilesAndExecutes()
    {
        // Arrange
        var expected = new List<int> { 1, 2, 3 };
        var node = new Constant(expected);

        // Act
        var expr = node.BuildExpression();
        var lambda = System.Linq.Expressions.Expression.Lambda<Func<List<int>>>(expr);
        var result = lambda.Compile()();

        // Assert
        await Assert.That(result).IsEqualTo(expected);
        await Assert.That(result.Count).IsEqualTo(3);
    }
}
