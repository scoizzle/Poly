using System.Linq.Expressions;
using Poly.Interpretation;
using Poly.Interpretation.Expressions;
using Poly.Interpretation.LinqInterpreter;
using ConditionalExpression = Poly.Interpretation.Expressions.ConditionalExpression;

namespace Poly.Tests.Interpretation;

public class BlockTests {
    [Test]
    public async Task Block_WithSingleExpression_ReturnsValue()
    {
        // Arrange
        var builder = new LinqExecutionPlanBuilder();
        var expression = new Constant(42);
        var block = new Block(expression);

        // Act
        var builtExpression = block.Evaluate(builder);
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
        var builder = new LinqExecutionPlanBuilder();
        var expr1 = new Constant(10);
        var expr2 = new Constant(20);
        var expr3 = new Constant(30);
        var block = new Block(expr1, expr2, expr3);

        // Act
        var builtExpression = block.Evaluate(builder);
        var lambda = Expression.Lambda<Func<int>>(builtExpression);
        var compiled = lambda.Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(30);
    }

    [Test]
    public async Task Block_WithArithmeticSequence_EvaluatesCorrectly()
    {
        // Arrange
        var builder = new LinqExecutionPlanBuilder();
        var typeDef = builder.GetTypeDefinition(typeof(int).FullName!);
        var param = builder.Parameter("x", typeDef);
        var paramRef = new NamedReference("x");

        // Block: { x + 1; x + 2; x + 3 }
        var expr1 = new BinaryOperation(BinaryOperationKind.Add, paramRef, new Constant(1));
        var expr2 = new BinaryOperation(BinaryOperationKind.Add, paramRef, new Constant(2));
        var expr3 = new BinaryOperation(BinaryOperationKind.Add, paramRef, new Constant(3));
        var block = new Block(expr1, expr2, expr3);

        // Act
        var builtExpression = block.Evaluate(builder);
        var lambda = Expression.Lambda<Func<int, int>>(builtExpression, param);
        var compiled = lambda.Compile();

        // Assert
        await Assert.That(compiled(10)).IsEqualTo(13);
        await Assert.That(compiled(5)).IsEqualTo(8);
    }

    [Test]
    public async Task Block_WithConditionalInside_WorksCorrectly()
    {
        // Arrange
        var builder = new LinqExecutionPlanBuilder();
        var typeDef = builder.GetTypeDefinition(typeof(int).FullName!);
        var param = builder.Parameter("x", typeDef);
        var paramRef = new NamedReference("x");

        // Block: { x > 10; x > 10 ? x : 0 }
        var condition = new BinaryOperation(BinaryOperationKind.GreaterThan, paramRef, new Constant(10));
        var conditional = new Poly.Interpretation.Expressions.ConditionalExpression(condition, paramRef, new Constant(0));
        var block = new Block(condition, conditional);

        // Act
        var builtExpression = block.Evaluate(builder);
        var lambda = Expression.Lambda<Func<int, int>>(builtExpression, param);
        var compiled = lambda.Compile();

        // Assert
        await Assert.That(compiled(15)).IsEqualTo(15);
        await Assert.That(compiled(5)).IsEqualTo(0);
    }

    [Test]
    public async Task Block_WithDifferentTypes_ReturnsLastExpressionType()
    {
        // Arrange
        var builder = new LinqExecutionPlanBuilder();

        // Block: { 42; "hello" }
        var intExpr = new Constant(42);
        var stringExpr = new Constant("hello");
        var block = new Block(intExpr, stringExpr);

        // Act
        var builtExpression = block.Evaluate(builder);
        var lambda = Expression.Lambda<Func<string>>(builtExpression);
        var compiled = lambda.Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo("hello");
    }

    [Test]
    public async Task Block_ToString_ReturnsExpectedFormat()
    {
        // Arrange
        var expr1 = new Constant(10);
        var expr2 = new Constant(20);
        var expr3 = new Constant(30);
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
    public async Task Block_WithComplexNestedStructure_EvaluatesCorrectly()
    {
        // Arrange
        var builder = new LinqExecutionPlanBuilder();
        var typeDef = builder.GetTypeDefinition(typeof(int).FullName!);
        var param = builder.Parameter("x", typeDef);
        var paramRef = new NamedReference("x");

        // Block: { x * 2; x * 2 > 10 ? x * 2 : 0 }
        var multiply = new BinaryOperation(BinaryOperationKind.Multiply, paramRef, new Constant(2));
        var condition = new BinaryOperation(BinaryOperationKind.GreaterThan, multiply, new Constant(10));
        var multiply2 = new BinaryOperation(BinaryOperationKind.Multiply, paramRef, new Constant(2));
        var conditional = new Poly.Interpretation.Expressions.ConditionalExpression(condition, multiply2, new Constant(0));
        var block = new Block(multiply, conditional);

        // Act
        var builtExpression = block.Evaluate(builder);
        var lambda = Expression.Lambda<Func<int, int>>(builtExpression, param);
        var compiled = lambda.Compile();

        // Assert
        await Assert.That(compiled(10)).IsEqualTo(20);  // 10 * 2 = 20 > 10, so return 20
        await Assert.That(compiled(3)).IsEqualTo(0);    // 3 * 2 = 6 <= 10, so return 0
    }
}
