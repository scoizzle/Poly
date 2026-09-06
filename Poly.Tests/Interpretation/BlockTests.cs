using Poly.Interpretation;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Introspection;
using Poly.Tests.TestHelpers;

using Expr = System.Linq.Expressions.Expression;

namespace Poly.Tests.Interpretation;

public class BlockTests {
    [Test]
    public async Task Block_WithSingleExpression_ReturnsValue() {
        // Arrange
        var node = new Block(Wrap(42));

        // Act
        var expr = node.BuildExpression();
        var compiled = Expr.Lambda<Func<int>>(expr).Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(42);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(42L);
    }

    [Test]
    public async Task Block_WithMultipleExpressions_ReturnsLastValue() {
        // Arrange
        var node = new Block(Wrap(10), Wrap(20), Wrap(99));

        // Act
        var expr = node.BuildExpression();
        var compiled = Expr.Lambda<Func<int>>(expr).Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(99);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(99L);
    }

    [Test]
    public async Task Block_WithVariableDeclaration_WorksCorrectly() {
        // Arrange - block with a variable that's assigned and used
        var varNode = new Variable("x");
        var assignNode = new Assignment(varNode, Wrap(50));
        var node = new Block([assignNode, varNode], [varNode]);

        // Act
        var expr = node.BuildExpression();
        var compiled = Expr.Lambda<Func<int>>(expr).Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(50);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(50L);
    }

    [Test]
    public async Task Block_WithArithmeticSequence_EvaluatesCorrectly() {
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
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(100L);
    }

    [Test]
    public async Task Block_WithConditionalInside_WorksCorrectly() {
        // Arrange
        var conditional = new Conditional(True, Wrap(55), Wrap(0));
        var node = new Block(conditional);

        // Act
        var expr = node.BuildExpression();
        var compiled = Expr.Lambda<Func<int>>(expr).Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(55);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(55L);
    }

    [Test]
    public async Task Block_WithDifferentTypes_ReturnsLastExpressionType() {
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
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(42L);
    }

    [Test]
    public async Task Block_GetTypeDefinition_ReturnsLastExpressionType() {
        var node = new Block(Wrap(10), Wrap(20));
        var analysis = Interpreter.Analyze(node);
        var resolved = analysis.GetResolvedType(node);
        await Assert.That(resolved).IsNotNull();
        await Assert.That(resolved!.GetRuntimeType()).IsEqualTo(typeof(int));
    }

    [Test]
    public async Task Block_ToString_ReturnsExpectedFormat() {
        // Arrange
        var node = new Block(Wrap(42));

        // Act
        var result = node.ToString();

        // Assert
        await Assert.That(result).IsNotNull();
    }

    [Test]
    public async Task Block_WithEmptyExpressions_IsAllowed() {
        // A4: Empty blocks are now allowed (codegen needs zero-entity OnModelCreating)
        var block = new Block(Array.Empty<Node>());
        await Assert.That(block.Nodes).IsEmpty();
        await Assert.That(block.Variables).IsEmpty();
    }

    [Test]
    public async Task Block_WithNullExpressions_ThrowsArgumentNullException() {
        // Act & Assert
        await Assert.That(() => new Block((Node[])null!)).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Block_WithNullVariables_ThrowsArgumentNullException() {
        // Act & Assert
        await Assert.That(() => new Block((IEnumerable<Node>)null!, [Wrap(42)])).Throws<ArgumentNullException>();
    }
}