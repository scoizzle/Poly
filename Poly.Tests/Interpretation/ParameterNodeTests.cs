using Poly.Interpretation.AbstractSyntaxTree;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.Interpretation;

/// <summary>
/// Unit tests for Parameter AST nodes and their LINQ expression compilation.
/// </summary>
public class ParameterNodeTests
{
    [Test]
    public async Task Parameter_IntType_CompilesAndExecutesWithValue()
    {
        // Arrange
        var param = new Parameter("x", TypeReference.To<int>());
        var paramExpr = param.GetParameterExpression();

        // Act
        var expr = param.BuildExpression();
        var lambda = System.Linq.Expressions.Expression.Lambda<Func<int, int>>(expr, paramExpr);
        var compiled = lambda.Compile();

        // Assert
        await Assert.That(compiled(42)).IsEqualTo(42);
        await Assert.That(compiled(100)).IsEqualTo(100);
    }

    [Test]
    public async Task Parameter_StringType_CompilesAndExecutesWithValue()
    {
        // Arrange
        var param = new Parameter("name", TypeReference.To<string>());
        var paramExpr = param.GetParameterExpression();

        // Act
        var expr = param.BuildExpression();
        var lambda = System.Linq.Expressions.Expression.Lambda<Func<string, string>>(expr, paramExpr);
        var compiled = lambda.Compile();

        // Assert
        await Assert.That(compiled("hello")).IsEqualTo("hello");
        await Assert.That(compiled("world")).IsEqualTo("world");
    }

    [Test]
    public async Task Parameter_DoubleType_CompilesAndExecutesWithValue()
    {
        // Arrange
        var param = new Parameter("value", TypeReference.To<double>());
        var paramExpr = param.GetParameterExpression();

        // Act
        var expr = param.BuildExpression();
        var lambda = System.Linq.Expressions.Expression.Lambda<Func<double, double>>(expr, paramExpr);
        var compiled = lambda.Compile();

        // Assert
        await Assert.That(compiled(3.14)).IsEqualTo(3.14);
        await Assert.That(compiled(2.71)).IsEqualTo(2.71);
    }

    [Test]
    public async Task Parameter_BoolType_CompilesAndExecutesWithValue()
    {
        // Arrange
        var param = new Parameter("flag", TypeReference.To<bool>());
        var paramExpr = param.GetParameterExpression();

        // Act
        var expr = param.BuildExpression();
        var lambda = System.Linq.Expressions.Expression.Lambda<Func<bool, bool>>(expr, paramExpr);
        var compiled = lambda.Compile();

        // Assert
        await Assert.That(compiled(true)).IsTrue();
        await Assert.That(compiled(false)).IsFalse();
    }

    [Test]
    public async Task Parameter_MultipleParameters_CompilesAndExecutes()
    {
        // Arrange
        var x = new Parameter("x", TypeReference.To<int>());
        var y = new Parameter("y", TypeReference.To<int>());
        var xExpr = x.GetParameterExpression();
        var yExpr = y.GetParameterExpression();

        // Act - Just return the first parameter
        var expr = x.BuildExpression();
        var lambda = System.Linq.Expressions.Expression.Lambda<Func<int, int, int>>(expr, xExpr, yExpr);
        var compiled = lambda.Compile();

        // Assert
        await Assert.That(compiled(10, 20)).IsEqualTo(10);
        await Assert.That(compiled(5, 15)).IsEqualTo(5);
    }

    [Test]
    public async Task Parameter_WithoutTypeHint_CompilesAsObject()
    {
        // Arrange
        var param = new Parameter("value");
        var paramExpr = param.GetParameterExpression();

        // Act
        var expr = param.BuildExpression();
        var lambda = System.Linq.Expressions.Expression.Lambda<Func<object, object>>(expr, paramExpr);
        var compiled = lambda.Compile();

        // Assert - Can accept any object
        await Assert.That(compiled(42)).IsEqualTo(42);
        await Assert.That(compiled("test")).IsEqualTo("test");
    }

    [Test]
    public async Task Parameter_SameParameterTwice_ReturnsSameExpression()
    {
        // Arrange
        var param = new Parameter("x", TypeReference.To<int>());

        // Act
        var expr1 = param.GetParameterExpression();
        var expr2 = param.GetParameterExpression();

        // Assert - Should return the same ParameterExpression instance
        await Assert.That(ReferenceEquals(expr1, expr2)).IsTrue();
    }
}
