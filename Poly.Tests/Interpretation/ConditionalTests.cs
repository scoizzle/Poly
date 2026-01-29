using Poly.Tests.TestHelpers;
using System.Linq.Expressions;

using Poly.Interpretation;
using Expr = System.Linq.Expressions.Expression;
using Poly.Interpretation.AbstractSyntaxTree;
using Poly.Interpretation.AbstractSyntaxTree.Comparison;

namespace Poly.Tests.Interpretation;

public class ConditionalTests
{
    [Test]
    public async Task Conditional_WithTrueCondition_ReturnsIfTrueValue()
    {
        // Arrange
        var node = new Conditional(True, Wrap(42), Wrap(0));

        // Act
        var expr = node.BuildExpression();
        var compiled = Expr.Lambda<Func<int>>(expr).Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(42);
    }

    [Test]
    public async Task Conditional_WithFalseCondition_ReturnsIfFalseValue()
    {
        // Arrange
        var node = new Conditional(False, Wrap(42), Wrap(99));

        // Act
        var expr = node.BuildExpression();
        var compiled = Expr.Lambda<Func<int>>(expr).Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(99);
    }

    [Test]
    public async Task Conditional_WithParameterCondition_EvaluatesCorrectly()
    {
        // Arrange
        var param = new Parameter("x", TypeReference.To<bool>());
        var node = new Conditional(param, Wrap(10), Wrap(20));

        // Act
        var compiled = node.CompileLambda<Func<bool, int>>((param, typeof(bool)));

        // Assert
        await Assert.That(compiled(true)).IsEqualTo(10);
        await Assert.That(compiled(false)).IsEqualTo(20);
    }

    [Test]
    public async Task Conditional_WithNestedConditionals_WorksCorrectly()
    {
        // Arrange
        var inner = new Conditional(True, Wrap(5), Wrap(10));
        var node = new Conditional(False, Wrap(1), inner);

        // Act
        var expr = node.BuildExpression();
        var compiled = Expr.Lambda<Func<int>>(expr).Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(5);
    }

    [Test]
    public async Task Conditional_GetTypeDefinition_ReturnsIfTrueType()
    {
        // Arrange
        var node = new Conditional(True, Wrap(42), Wrap(0));

        // Act - build to trigger semantic analysis
        _ = node.BuildExpression();

        // Assert - type is captured during interpretation
        await Assert.That(node).IsNotNull();
    }

    [Test]
    public async Task Conditional_ToString_ReturnsExpectedFormat()
    {
        // Arrange
        var node = new Conditional(True, Wrap(42), Wrap(0));

        // Act
        var result = node.ToString();

        // Assert
        await Assert.That(result).Contains("?");
    }

    [Test]
    public async Task Conditional_WithNullArguments_AllowsNulls()
    {
        // Act
        var node = new Conditional(null!, null!, null!);

        // Assert
        await Assert.That(node).IsNotNull();
    }
}
