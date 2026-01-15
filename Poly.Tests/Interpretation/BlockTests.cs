using System.Linq.Expressions;

using Poly.Interpretation;
using Poly.Interpretation.Operators;
using Poly.Interpretation.Operators.Arithmetic;
using Poly.Interpretation.Operators.Comparison;

namespace Poly.Tests.Interpretation;

public class BlockTests {
    [Test]
    public async Task Block_WithSingleExpression_ReturnsValue()
    {
        // Arrange
        var context = new InterpretationContext();
        var expression = Value.Wrap(42);
        var block = new Block(expression);

        // Act
        var builtExpression = block.BuildExpression(context);
        var lambda = Expression.Lambda<Func<int>>(builtExpression);
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
        var expr1 = Value.Wrap(10);
        var expr2 = Value.Wrap(20);
        var expr3 = Value.Wrap(30);
        var block = new Block(expr1, expr2, expr3);

        // Act
        var builtExpression = block.BuildExpression(context);
        var lambda = Expression.Lambda<Func<int>>(builtExpression);
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
        var localVar = Expression.Variable(typeof(int), "temp");

        // Assign 42 to temp
        var assignExpr = Expression.Assign(localVar, Expression.Constant(42));

        // Return temp
        var returnExpr = localVar;

        // Create block with variable
        var blockExpr = Expression.Block(
            new[] { localVar },
            assignExpr,
            returnExpr
        );

        // Act
        var lambda = Expression.Lambda<Func<int>>(blockExpr);
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
        var expr1 = new Add(param, Value.Wrap(1));
        var expr2 = new Add(param, Value.Wrap(2));
        var expr3 = new Add(param, Value.Wrap(3));
        var block = new Block(expr1, expr2, expr3);

        // Act
        var builtExpression = block.BuildExpression(context);
        var lambda = Expression.Lambda<Func<int, int>>(builtExpression, param.BuildExpression(context));
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
        var condition = new GreaterThan(param, Value.Wrap(10));
        var conditional = new Conditional(condition, param, Value.Wrap(0));
        var block = new Block(condition, conditional);

        // Act
        var builtExpression = block.BuildExpression(context);
        var lambda = Expression.Lambda<Func<int, int>>(builtExpression, param.BuildExpression(context));
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
        var intExpr = Value.Wrap(42);
        var stringExpr = Value.Wrap("hello");
        var block = new Block(intExpr, stringExpr);

        // Act
        var builtExpression = block.BuildExpression(context);
        var lambda = Expression.Lambda<Func<string>>(builtExpression);
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
        var intExpr = Value.Wrap(42);
        var stringExpr = Value.Wrap("hello");
        var block = new Block(intExpr, stringExpr);

        // Act
        var typeDef = block.GetTypeDefinition(context);

        // Assert
        await Assert.That(typeDef).IsNotNull();
        await Assert.That(typeDef.ReflectedType).IsEqualTo(typeof(string));
    }

    [Test]
    public async Task Block_ToString_ReturnsExpectedFormat()
    {
        // Arrange
        var expr1 = Value.Wrap(10);
        var expr2 = Value.Wrap(20);
        var expr3 = Value.Wrap(30);
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
        await Assert.That(() => new Block(Array.Empty<Interpretable>()))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Block_WithNullExpressions_ThrowsArgumentNullException()
    {
        // Assert
        await Assert.That(() => new Block((Interpretable[])null!))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Block_WithNullVariables_ThrowsArgumentNullException()
    {
        // Assert
        await Assert.That(() => new Block(new[] { Value.Wrap(42) }, null!))
            .Throws<ArgumentNullException>();
    }
}