using Poly.Tests.TestHelpers;
using System.Linq.Expressions;

using Poly.Interpretation;
using Expr = System.Linq.Expressions.Expression;
using Poly.Interpretation.AbstractSyntaxTree;
using Poly.Interpretation.AbstractSyntaxTree.Arithmetic;

namespace Poly.Tests.Interpretation;

public class FluentValueApiTests
{
    [Test]
    public async Task FluentApi_ArithmeticChaining_EvaluatesCorrectly()
    {
        // Arrange - fluent chaining: (10 + 5) * 2
        var node = Wrap(10).Add(Wrap(5)).Multiply(Wrap(2));

        // Act
        var expr = node.BuildExpression();
        var compiled = Expr.Lambda<Func<int>>(expr).Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(30);
    }

    [Test]
    public async Task FluentApi_ComparisonChaining_EvaluatesCorrectly()
    {
        // Arrange - fluent chaining: 10 > 5
        var node = Wrap(10).GreaterThan(Wrap(5));

        // Act
        var expr = node.BuildExpression();
        var compiled = Expr.Lambda<Func<bool>>(expr).Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task FluentApi_BooleanChaining_EvaluatesCorrectly()
    {
        // Arrange - fluent chaining: true && false
        var node = True.And(False);

        // Act
        var expr = node.BuildExpression();
        var compiled = Expr.Lambda<Func<bool>>(expr).Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task FluentApi_ConditionalExpression_EvaluatesCorrectly()
    {
        // Arrange - fluent chaining: true ? 42 : 0
        var node = True.Conditional(Wrap(42), Wrap(0));

        // Act
        var expr = node.BuildExpression();
        var compiled = Expr.Lambda<Func<int>>(expr).Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(42);
    }

    [Test]
    public async Task FluentApi_CoalesceExpression_EvaluatesCorrectly()
    {
        // Arrange - fluent chaining: null ?? 42
        var node = Wrap(null as int?).Coalesce(Wrap(42));

        // Act
        var expr = node.BuildExpression();
        var compiled = Expr.Lambda<Func<int>>(expr).Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(42);
    }

    [Test]
    public async Task FluentApi_NegateOperation_EvaluatesCorrectly()
    {
        // Arrange - fluent chaining: -42
        var node = Wrap(42).Negate();

        // Act
        var expr = node.BuildExpression();
        var compiled = Expr.Lambda<Func<int>>(expr).Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(-42);
    }

    [Test]
    public async Task FluentApi_NotOperation_EvaluatesCorrectly()
    {
        // Arrange - fluent chaining: !true
        var node = True.Not();

        // Act
        var expr = node.BuildExpression();
        var compiled = Expr.Lambda<Func<bool>>(expr).Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task FluentApi_TypeCastOperation_EvaluatesCorrectly()
    {
        // Arrange - fluent chaining: (int)42.0 cast to double
        var node = Wrap(42).CastTo("System.Double");

        // Act
        var expr = node.BuildExpression();
        var compiled = Expr.Lambda<Func<double>>(expr).Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(42.0);
    }

    [Test]
    public async Task FluentApi_ComplexExpression_EvaluatesCorrectly()
    {
        // Arrange - fluent chaining: ((10 + 5) * 2) > 20
        var node = Wrap(10)
            .Add(Wrap(5))
            .Multiply(Wrap(2))
            .GreaterThan(Wrap(20));

        // Act
        var expr = node.BuildExpression();
        var compiled = Expr.Lambda<Func<bool>>(expr).Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task FluentApi_MemberAccess_WorksCorrectly()
    {
        // Arrange
        var str = "hello";
        var node = Wrap(str).GetMember("Length");

        // Act
        var expr = node.BuildExpression();
        var compiled = Expr.Lambda<Func<int>>(expr).Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(5);
    }
}
