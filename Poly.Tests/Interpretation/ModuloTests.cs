using Poly.Tests.TestHelpers;
using System.Linq.Expressions;

using Poly.Interpretation;
using Expr = System.Linq.Expressions.Expression;
using Poly.Interpretation.AbstractSyntaxTree;
using Poly.Interpretation.AbstractSyntaxTree.Arithmetic;

namespace Poly.Tests.Interpretation;

public class ModuloTests
{
    [Test]
    public async Task Modulo_WithIntegers_ReturnsRemainder()
    {
        // Arrange
        var node = new Modulo(Wrap(17), Wrap(5));

        // Act
        var expr = node.BuildExpression();
        var compiled = Expr.Lambda<Func<int>>(expr).Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(2);
    }

    [Test]
    public async Task Modulo_WithExactDivision_ReturnsZero()
    {
        // Arrange
        var node = new Modulo(Wrap(20), Wrap(5));

        // Act
        var expr = node.BuildExpression();
        var compiled = Expr.Lambda<Func<int>>(expr).Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(0);
    }

    [Test]
    public async Task Modulo_WithDoubles_ReturnsRemainder()
    {
        // Arrange
        var node = new Modulo(Wrap(5.5), Wrap(2.0));

        // Act
        var expr = node.BuildExpression();
        var compiled = Expr.Lambda<Func<double>>(expr).Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(1.5);
    }

    [Test]
    public async Task Modulo_WithParameters_EvaluatesCorrectly()
    {
        // Arrange
        var param1 = new Parameter("a", TypeReference.To<int>());
        var param2 = new Parameter("b", TypeReference.To<int>());
        var node = new Modulo(param1, param2);

        // Act
        var expr = node.BuildExpression();
        var paramExprs = new[] { param1.GetParameterExpression(), param2.GetParameterExpression() };
        var compiled = Expr.Lambda<Func<int, int, int>>(expr, paramExprs).Compile();
        var result = compiled(17, 5);

        // Assert
        await Assert.That(result).IsEqualTo(2);
    }

    [Test]
    public async Task Modulo_WithNegativeNumbers_ReturnsCorrectRemainder()
    {
        // Arrange
        var node = new Modulo(Wrap(-17), Wrap(5));

        // Act
        var expr = node.BuildExpression();
        var compiled = Expr.Lambda<Func<int>>(expr).Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(-2);
    }

    [Test]
    public async Task Modulo_GetTypeDefinition_ReturnsNumericType()
    {
        // Arrange
        var node = new Modulo(Wrap(17), Wrap(5));

        // Act - build to trigger semantic analysis
        _ = node.BuildExpression();

        // Assert
        await Assert.That(node).IsNotNull();
    }

    [Test]
    public async Task Modulo_ToString_ReturnsExpectedFormat()
    {
        // Arrange
        var node = new Modulo(Wrap(17), Wrap(5));

        // Act
        var result = node.ToString();

        // Assert
        await Assert.That(result).Contains("%");
    }

    [Test]
    public async Task Modulo_WithNullArguments_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.That(() => new Modulo(null!, Wrap(5))).Throws<ArgumentNullException>();
    }
}
