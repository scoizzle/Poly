using Poly.Interpretation.AbstractSyntaxTree;
using Poly.Interpretation.AbstractSyntaxTree.Arithmetic;
using Poly.Interpretation.AbstractSyntaxTree.Comparison;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.Interpretation;

/// <summary>
/// Unit tests for conditional and control flow AST nodes.
/// </summary>
public class ConditionalNodeTests
{
    // Conditional (Ternary) Tests
    [Test]
    public async Task Conditional_TrueCondition_ReturnsIfTrueValue()
    {
        // Arrange
        var node = new Conditional(
            new Constant(true),
            new Constant(42),
            new Constant(0));

        // Act
        var expr = node.BuildExpression();
        var lambda = System.Linq.Expressions.Expression.Lambda<Func<int>>(expr);
        var result = lambda.Compile()();

        // Assert
        await Assert.That(result).IsEqualTo(42);
    }

    [Test]
    public async Task Conditional_FalseCondition_ReturnsIfFalseValue()
    {
        // Arrange
        var node = new Conditional(
            new Constant(false),
            new Constant(42),
            new Constant(99));

        // Act
        var expr = node.BuildExpression();
        var lambda = System.Linq.Expressions.Expression.Lambda<Func<int>>(expr);
        var result = lambda.Compile()();

        // Assert
        await Assert.That(result).IsEqualTo(99);
    }

    [Test]
    public async Task Conditional_WithParameter_EvaluatesBasedOnParameter()
    {
        // Arrange
        var param = new Parameter("condition", TypeReference.To<bool>());
        var node = new Conditional(
            param,
            new Constant("yes"),
            new Constant("no"));
        var paramExpr = param.GetParameterExpression();

        // Act
        var expr = node.BuildExpression();
        var lambda = System.Linq.Expressions.Expression.Lambda<Func<bool, string>>(expr, paramExpr);
        var compiled = lambda.Compile();

        // Assert
        await Assert.That(compiled(true)).IsEqualTo("yes");
        await Assert.That(compiled(false)).IsEqualTo("no");
    }

    [Test]
    public async Task Conditional_WithComparison_EvaluatesCorrectly()
    {
        // Arrange - if (x > 5) then 10 else 0
        var param = new Parameter("x", TypeReference.To<int>());
        var comparison = new GreaterThan(param, new Constant(5));
        var node = new Conditional(
            comparison,
            new Constant(10),
            new Constant(0));
        var paramExpr = param.GetParameterExpression();

        // Act
        var expr = node.BuildExpression();
        var lambda = System.Linq.Expressions.Expression.Lambda<Func<int, int>>(expr, paramExpr);
        var compiled = lambda.Compile();

        // Assert
        await Assert.That(compiled(10)).IsEqualTo(10);
        await Assert.That(compiled(3)).IsEqualTo(0);
    }

    [Test]
    public async Task Conditional_NestedConditionals_EvaluatesCorrectly()
    {
        // Arrange - if (true) then (if (true) then 1 else 2) else 3
        var inner = new Conditional(
            new Constant(true),
            new Constant(1),
            new Constant(2));
        var outer = new Conditional(
            new Constant(true),
            inner,
            new Constant(3));

        // Act
        var expr = outer.BuildExpression();
        var lambda = System.Linq.Expressions.Expression.Lambda<Func<int>>(expr);
        var result = lambda.Compile()();

        // Assert
        await Assert.That(result).IsEqualTo(1);
    }

    // Coalesce Tests
    [Test]
    public async Task Coalesce_NullValue_ReturnsDefaultValue()
    {
        // Arrange
        var node = new Coalesce(new Constant(null), new Constant(42));

        // Act
        var expr = node.BuildExpression();
        var lambda = System.Linq.Expressions.Expression.Lambda<Func<int>>(expr);
        var result = lambda.Compile()();

        // Assert
        await Assert.That(result).IsEqualTo(42);
    }

    [Test]
    public async Task Coalesce_NonNullValue_ReturnsOriginalValue()
    {
        // Arrange
        var node = new Coalesce(new Constant(10), new Constant(42));

        // Act
        var expr = node.BuildExpression();
        var lambda = System.Linq.Expressions.Expression.Lambda<Func<int>>(expr);
        var result = lambda.Compile()();

        // Assert
        await Assert.That(result).IsEqualTo(10);
    }

    [Test]
    public async Task Coalesce_WithNullableParameter_ReturnsCorrectValue()
    {
        // Arrange - x ?? 100
        var param = new Parameter("x", TypeReference.To<int?>());
        var node = new Coalesce(param, new Constant(100));
        var paramExpr = param.GetParameterExpression();

        // Act
        var expr = node.BuildExpression();
        var lambda = System.Linq.Expressions.Expression.Lambda<Func<int?, int>>(expr, paramExpr);
        var compiled = lambda.Compile();

        // Assert
        await Assert.That(compiled(null)).IsEqualTo(100);
        await Assert.That(compiled(50)).IsEqualTo(50);
        await Assert.That(compiled(0)).IsEqualTo(0);
    }

    [Test]
    public async Task Coalesce_ChainedCoalesce_ReturnsFirstNonNull()
    {
        // Arrange - null ?? null ?? 42
        var inner = new Coalesce(new Constant(null), new Constant(null));
        var outer = new Coalesce(inner, new Constant(42));

        // Act
        var expr = outer.BuildExpression();
        var lambda = System.Linq.Expressions.Expression.Lambda<Func<int>>(expr);
        var result = lambda.Compile()();

        // Assert
        await Assert.That(result).IsEqualTo(42);
    }

    // UnaryMinus Tests
    [Test]
    public async Task UnaryMinus_PositiveInteger_ReturnsNegative()
    {
        // Arrange
        var node = new UnaryMinus(new Constant(42));

        // Act
        var expr = node.BuildExpression();
        var lambda = System.Linq.Expressions.Expression.Lambda<Func<int>>(expr);
        var result = lambda.Compile()();

        // Assert
        await Assert.That(result).IsEqualTo(-42);
    }

    [Test]
    public async Task UnaryMinus_NegativeInteger_ReturnsPositive()
    {
        // Arrange
        var node = new UnaryMinus(new Constant(-10));

        // Act
        var expr = node.BuildExpression();
        var lambda = System.Linq.Expressions.Expression.Lambda<Func<int>>(expr);
        var result = lambda.Compile()();

        // Assert
        await Assert.That(result).IsEqualTo(10);
    }

    [Test]
    public async Task UnaryMinus_WithParameter_NegatesValue()
    {
        // Arrange
        var param = new Parameter("x", TypeReference.To<int>());
        var node = new UnaryMinus(param);
        var paramExpr = param.GetParameterExpression();

        // Act
        var expr = node.BuildExpression();
        var lambda = System.Linq.Expressions.Expression.Lambda<Func<int, int>>(expr, paramExpr);
        var compiled = lambda.Compile();

        // Assert
        await Assert.That(compiled(5)).IsEqualTo(-5);
        await Assert.That(compiled(-8)).IsEqualTo(8);
    }

    [Test]
    public async Task UnaryMinus_DoubleValue_NegatesCorrectly()
    {
        // Arrange
        var node = new UnaryMinus(new Constant(3.14));

        // Act
        var expr = node.BuildExpression();
        var lambda = System.Linq.Expressions.Expression.Lambda<Func<double>>(expr);
        var result = lambda.Compile()();

        // Assert
        await Assert.That(result).IsEqualTo(-3.14);
    }

    [Test]
    public async Task UnaryMinus_NestedInArithmetic_EvaluatesCorrectly()
    {
        // Arrange - 10 + (-5) = 5
        var negated = new UnaryMinus(new Constant(5));
        var node = new Add(new Constant(10), negated);

        // Act
        var expr = node.BuildExpression();
        var lambda = System.Linq.Expressions.Expression.Lambda<Func<int>>(expr);
        var result = lambda.Compile()();

        // Assert
        await Assert.That(result).IsEqualTo(5);
    }
}
