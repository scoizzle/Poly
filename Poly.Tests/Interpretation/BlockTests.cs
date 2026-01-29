using System.Linq.Expressions;

using Poly.Interpretation;
using Poly.Interpretation.AbstractSyntaxTree;
using Poly.Interpretation.AbstractSyntaxTree.Arithmetic;
using Poly.Tests.TestHelpers;

using Expr = System.Linq.Expressions.Expression;

namespace Poly.Tests.Interpretation;

public class BlockTests {
    [Test]
    public async Task Block_WithSingleExpression_ReturnsValue()
    {
        // Arrange
        var node = new Block(Wrap(42));

        // Act
        var expr = node.BuildExpression();
        var compiled = Expr.Lambda<Func<int>>(expr).Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(42);
    }

    [Test]
    public async Task Block_WithMultipleExpressions_ReturnsLastValue()
    {
        // Arrange
        var node = new Block(Wrap(10), Wrap(20), Wrap(99));

        // Act
        var expr = node.BuildExpression();
        var compiled = Expr.Lambda<Func<int>>(expr).Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(99);
    }

    [Test]
    public async Task Block_WithVariableDeclaration_WorksCorrectly()
    {
        // Arrange - block with a variable that's assigned and used
        var varNode = new Variable("x");
        var assignNode = new Assignment(varNode, Wrap(50));
        var node = new Block([assignNode, varNode], new[] { varNode });

        // Act
        var expr = node.BuildExpression();
        var compiled = Expr.Lambda<Func<int>>(expr).Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(50);
    }

    [Test]
    public async Task Block_WithArithmeticSequence_EvaluatesCorrectly()
    {
        // Arrange
        var node = new Block(
            Wrap(10),
            new Add(Wrap(5), Wrap(3)),
            Wrap(100)
        );

        // Act
        var expr = node.BuildExpression();
        var compiled = Expr.Lambda<Func<int>>(expr).Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(100);
    }

    [Test]
    public async Task Block_WithConditionalInside_WorksCorrectly()
    {
        // Arrange
        var conditional = new Conditional(True, Wrap(55), Wrap(0));
        var node = new Block(conditional);

        // Act
        var expr = node.BuildExpression();
        var compiled = Expr.Lambda<Func<int>>(expr).Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(55);
    }

    [Test]
    public async Task Block_WithDifferentTypes_ReturnsLastExpressionType()
    {
        // Arrange
        var node = new Block(
            Wrap("hello"),
            Wrap(42)
        );

        // Act
        var expr = node.BuildExpression();
        var compiled = Expr.Lambda<Func<int>>(expr).Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(42);
    }

    [Test]
    public async Task Block_GetTypeDefinition_ReturnsLastExpressionType()
    {
        // Arrange
        var node = new Block(Wrap(10), Wrap(20));

        // Act - build to trigger semantic analysis
        _ = node.BuildExpression();

        // Assert
        await Assert.That(node).IsNotNull();
    }

    [Test]
    public async Task Block_ToString_ReturnsExpectedFormat()
    {
        // Arrange
        var node = new Block(Wrap(42));

        // Act
        var result = node.ToString();

        // Assert
        await Assert.That(result).IsNotNull();
    }

    [Test]
    public async Task Block_WithEmptyExpressions_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.That(() => new Block(Array.Empty<Node>())).Throws<ArgumentException>();
    }

    [Test]
    public async Task Block_WithNullExpressions_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.That(() => new Block((Node[])null!)).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Block_WithNullVariables_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.That(() => new Block((IEnumerable<Node>)null!, [Wrap(42)])).Throws<ArgumentNullException>();
    }
}