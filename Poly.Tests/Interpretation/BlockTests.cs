using Poly.Tests.TestHelpers;
using System.Linq.Expressions;

using Poly.Interpretation;
using Poly.Interpretation.AbstractSyntaxTree;
using Expr = System.Linq.Expressions.Expression;
using Poly.Interpretation.AbstractSyntaxTree.Arithmetic;
using Poly.Interpretation.AbstractSyntaxTree.Comparison;

namespace Poly.Tests.Interpretation;

public class BlockTests {
    [Test]
    public async Task Block_WithSingleExpression_ReturnsValue()
    {
        // Arrange
        var context = new InterpretationContext();
        var expression = Wrap(42);
        var block = new Block(expression);

        // Act
        var builtExpression = block.BuildExpression(context);
        var lambda = Expr.Lambda<Func<int>>(builtExpression);
        var compiled = lambda.Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(42);
    }

    [Test]
    public async Task Block_WithMultipleExpressions_ReturnsLastValue()
    {
        // Arrange
        var context = new InterpretationContext();
        var expr1 = Wrap(10);
        var expr2 = Wrap(20);
        var expr3 = Wrap(30);
        var block = new Block(expr1, expr2, expr3);

        // Act
        var builtExpression = block.BuildExpression(context);
        var lambda = Expr.Lambda<Func<int>>(builtExpression);
        var compiled = lambda.Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(30);
    }

    [Test]
    public async Task Block_WithVariableDeclaration_WorksCorrectly()
    {
        // Arrange
        var context = new InterpretationContext();

        // Create a local variable
        var localVar = Expr.Variable(typeof(int), "temp");

        // Assign 42 to temp
        var assignExpr = Expr.Assign(localVar, Expr.Constant(42));

        // Return temp
        var returnExpr = localVar;

        // Create block with variable
        var blockExpr = Expr.Block(
            new[] { localVar },
            assignExpr,
            returnExpr
        );

        // Act
        var lambda = Expr.Lambda<Func<int>>(blockExpr);
        var compiled = lambda.Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(42);
    }

    [Test]
    public async Task Block_WithArithmeticSequence_EvaluatesCorrectly()
    {
        // Arrange
        var context = new InterpretationContext();
        var param = context.AddParameter<int>("x");

        // Block: { x + 1; x + 2; x + 3 }
        var expr1 = new Add(param, Wrap(1));
        var expr2 = new Add(param, Wrap(2));
        var expr3 = new Add(param, Wrap(3));
        var block = new Block(expr1, expr2, expr3);

        // Act
        var builtExpression = block.BuildExpression(context);
        var lambda = Expr.Lambda<Func<int, int>>(builtExpression, param.GetParameterExpression(context));
        var compiled = lambda.Compile();

        // Assert
        await Assert.That(compiled(10)).IsEqualTo(13);
        await Assert.That(compiled(5)).IsEqualTo(8);
    }

    [Test]
    public async Task Block_WithConditionalInside_WorksCorrectly()
    {
        // Arrange
        var context = new InterpretationContext();
        var param = context.AddParameter<int>("x");

        // Block: { x > 10; x > 10 ? x : 0 }
        var condition = new GreaterThan(param, Wrap(10));
        var conditional = new Conditional(condition, param, Wrap(0));
        var block = new Block(condition, conditional);

        // Act
        var builtExpression = block.BuildExpression(context);
        var lambda = Expr.Lambda<Func<int, int>>(builtExpression, param.GetParameterExpression(context));
        var compiled = lambda.Compile();

        // Assert
        await Assert.That(compiled(15)).IsEqualTo(15);
        await Assert.That(compiled(5)).IsEqualTo(0);
    }

    [Test]
    public async Task Block_WithDifferentTypes_ReturnsLastExpressionType()
    {
        // Arrange
        var context = new InterpretationContext();

        // Block: { 42; "hello" }
        var intExpr = Wrap(42);
        var stringExpr = Wrap("hello");
        var block = new Block(intExpr, stringExpr);

        // Act
        var builtExpression = block.BuildExpression(context);
        var lambda = Expr.Lambda<Func<string>>(builtExpression);
        var compiled = lambda.Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo("hello");
    }

    [Test]
    public async Task Block_GetTypeDefinition_ReturnsLastExpressionType()
    {
        // Arrange
        var context = new InterpretationContext();
        var intExpr = Wrap(42);
        var stringExpr = Wrap("hello");
        var block = new Block(intExpr, stringExpr);

        // Act
        var typeDef = block.GetResolvedType(context);

        // Assert
        await Assert.That(typeDef).IsNotNull();
        await Assert.That(typeDef.ReflectedType).IsEqualTo(typeof(string));
    }

    [Test]
    public async Task Block_ToString_ReturnsExpectedFormat()
    {
        // Arrange
        var expr1 = Wrap(10);
        var expr2 = Wrap(20);
        var expr3 = Wrap(30);
        var block = new Block(expr1, expr2, expr3);

        // Act
        var result = block.ToString();

        // Assert
        await Assert.That(result).Contains("10");
        await Assert.That(result).Contains("20");
        await Assert.That(result).Contains("30");
        await Assert.That(result).Contains("{");
        await Assert.That(result).Contains("}");
    }

    [Test]
    public async Task Block_WithEmptyExpressions_ThrowsArgumentException()
    {
        // Assert
        await Assert.That(() => new Block(Array.Empty<Node>()))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Block_WithNullExpressions_ThrowsArgumentNullException()
    {
        // Assert
        await Assert.That(() => new Block((Node[])null!))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Block_WithNullVariables_ThrowsArgumentNullException()
    {
        // Assert
        await Assert.That(() => new Block(new[] { Wrap(42) }, null!))
            .Throws<ArgumentNullException>();
    }
}