using System.Linq.Expressions;
using Poly.Interpretation;
using Poly.Interpretation.Expressions;
using Poly.Interpretation.LinqInterpreter;
using ConditionalExpression = Poly.Interpretation.Expressions.ConditionalExpression;

namespace Poly.Tests.Interpretation;

public class ConditionalTests {
    [Test]
    public async Task Conditional_WithTrueCondition_ReturnsIfTrueValue()
    {
        // Arrange
        var builder = new LinqExecutionPlanBuilder();
        var condition = new Constant(true);
        var ifTrue = new Constant(42);
        var ifFalse = new Constant(0);
        var conditional = new ConditionalExpression(condition, ifTrue, ifFalse);

        // Act
        var expression = conditional.Evaluate(builder);
        var lambda = Expression.Lambda<Func<int>>(expression);
        var compiled = lambda.Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(42);
    }

    [Test]
    public async Task Conditional_WithFalseCondition_ReturnsIfFalseValue()
    {
        // Arrange
        var builder = new LinqExecutionPlanBuilder();
        var condition = new Constant(false);
        var ifTrue = new Constant(42);
        var ifFalse = new Constant(99);
        var conditional = new ConditionalExpression(condition, ifTrue, ifFalse);

        // Act
        var expression = conditional.Evaluate(builder);
        var lambda = Expression.Lambda<Func<int>>(expression);
        var compiled = lambda.Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(99);
    }

    [Test]
    public async Task Conditional_WithParameterCondition_EvaluatesCorrectly()
    {
        // Arrange
        var builder = new LinqExecutionPlanBuilder();
        var typeDef = builder.GetTypeDefinition(typeof(int).FullName!);
        var param = builder.Parameter("x", typeDef);
        var paramRef = new NamedReference("x");

        // x > 10 ? "big" : "small"
        var condition = new BinaryOperation(BinaryOperationKind.GreaterThan, paramRef, new Constant(10));
        var ifTrue = new Constant("big");
        var ifFalse = new Constant("small");
        var conditional = new ConditionalExpression(condition, ifTrue, ifFalse);

        // Act
        var expression = conditional.Evaluate(builder);
        var lambda = Expression.Lambda<Func<int, string>>(expression, param);
        var compiled = lambda.Compile();

        // Assert
        await Assert.That(compiled(15)).IsEqualTo("big");
        await Assert.That(compiled(5)).IsEqualTo("small");
        await Assert.That(compiled(10)).IsEqualTo("small");
    }

    [Test]
    public async Task Conditional_WithNestedConditionals_WorksCorrectly()
    {
        // Arrange
        var builder = new LinqExecutionPlanBuilder();
        var typeDef = builder.GetTypeDefinition(typeof(int).FullName!);
        var param = builder.Parameter("x", typeDef);
        var paramRef = new NamedReference("x");

        // x < 0 ? "negative" : (x > 0 ? "positive" : "zero")
        var lessThanZero = new BinaryOperation(BinaryOperationKind.LessThan, paramRef, new Constant(0));
        var greaterThanZero = new BinaryOperation(BinaryOperationKind.GreaterThan, paramRef, new Constant(0));
        var innerConditional = new Poly.Interpretation.Expressions.ConditionalExpression(greaterThanZero, new Constant("positive"), new Constant("zero"));
        var outerConditional = new Poly.Interpretation.Expressions.ConditionalExpression(lessThanZero, new Constant("negative"), innerConditional);

        // Act
        var expression = outerConditional.Evaluate(builder);
        var lambda = Expression.Lambda<Func<int, string>>(expression, param);
        var compiled = lambda.Compile();

        // Assert
        await Assert.That(compiled(-5)).IsEqualTo("negative");
        await Assert.That(compiled(5)).IsEqualTo("positive");
        await Assert.That(compiled(0)).IsEqualTo("zero");
    }

    [Test]
    public async Task Conditional_WithComplexExpressions_EvaluatesCorrectly()
    {
        // Arrange
        var builder = new LinqExecutionPlanBuilder();
        var typeDef = builder.GetTypeDefinition(typeof(int).FullName!);
        var param = builder.Parameter("x", typeDef);
        var paramRef = new NamedReference("x");

        // x % 2 == 0 ? x * 2 : x * 3
        var modulo = new BinaryOperation(BinaryOperationKind.Modulo, paramRef, new Constant(2));
        var isEven = new BinaryOperation(BinaryOperationKind.Equal, modulo, new Constant(0));
        var doubleX = new BinaryOperation(BinaryOperationKind.Multiply, paramRef, new Constant(2));
        var tripleX = new BinaryOperation(BinaryOperationKind.Multiply, paramRef, new Constant(3));
        var conditional = new Poly.Interpretation.Expressions.ConditionalExpression(isEven, doubleX, tripleX);

        // Act
        var expression = conditional.Evaluate(builder);
        var lambda = Expression.Lambda<Func<int, int>>(expression, param);
        var compiled = lambda.Compile();

        // Assert
        await Assert.That(compiled(4)).IsEqualTo(8);   // even: 4 * 2
        await Assert.That(compiled(5)).IsEqualTo(15);  // odd: 5 * 3
    }

    [Test]
    public async Task Conditional_WithNullArguments_ThrowsArgumentNullException()
    {
        // Assert
        await Assert.That(() => new Poly.Interpretation.Expressions.ConditionalExpression(null!, new Constant(1), new Constant(2)))
            .Throws<ArgumentNullException>();
        await Assert.That(() => new Poly.Interpretation.Expressions.ConditionalExpression(new Constant(true), null!, new Constant(2)))
            .Throws<ArgumentNullException>();
        await Assert.That(() => new Poly.Interpretation.Expressions.ConditionalExpression(new Constant(true), new Constant(1), null!))
            .Throws<ArgumentNullException>();
    }
}
