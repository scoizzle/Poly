using Poly.Interpretation;
using Poly.Interpretation.Operators;
using Poly.Interpretation.Operators.Comparison;
using Poly.Interpretation.Operators.Equality;
using System.Linq.Expressions;

namespace Poly.Tests.Interpretation;

public class ConditionalTests {
    [Test]
    public async Task Conditional_WithTrueCondition_ReturnsIfTrueValue() {
        // Arrange
        var context = new InterpretationContext();
        var condition = Value.True;
        var ifTrue = Value.Wrap(42);
        var ifFalse = Value.Wrap(0);
        var conditional = new Conditional(condition, ifTrue, ifFalse);

        // Act
        var expression = conditional.BuildExpression(context);
        var lambda = Expression.Lambda<Func<int>>(expression);
        var compiled = lambda.Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(42);
    }

    [Test]
    public async Task Conditional_WithFalseCondition_ReturnsIfFalseValue() {
        // Arrange
        var context = new InterpretationContext();
        var condition = Value.False;
        var ifTrue = Value.Wrap(42);
        var ifFalse = Value.Wrap(99);
        var conditional = new Conditional(condition, ifTrue, ifFalse);

        // Act
        var expression = conditional.BuildExpression(context);
        var lambda = Expression.Lambda<Func<int>>(expression);
        var compiled = lambda.Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(99);
    }

    [Test]
    public async Task Conditional_WithParameterCondition_EvaluatesCorrectly() {
        // Arrange
        var context = new InterpretationContext();
        var intTypeDef = context.GetTypeDefinition<int>();
        var param = context.AddParameter<int>("x");
        
        // x > 10 ? "big" : "small"
        var condition = new GreaterThan(param, Value.Wrap(10));
        var ifTrue = Value.Wrap("big");
        var ifFalse = Value.Wrap("small");
        var conditional = new Conditional(condition, ifTrue, ifFalse);

        // Act
        var expression = conditional.BuildExpression(context);
        var lambda = Expression.Lambda<Func<int, string>>(expression, param.BuildExpression(context));
        var compiled = lambda.Compile();

        // Assert
        await Assert.That(compiled(15)).IsEqualTo("big");
        await Assert.That(compiled(5)).IsEqualTo("small");
        await Assert.That(compiled(10)).IsEqualTo("small");
    }

    [Test]
    public async Task Conditional_WithNestedConditionals_WorksCorrectly() {
        // Arrange
        var context = new InterpretationContext();
        var param = context.AddParameter<int>("x");
        
        // x < 0 ? "negative" : (x > 0 ? "positive" : "zero")
        var lessThanZero = new LessThan(param, Value.Wrap(0));
        var greaterThanZero = new GreaterThan(param, Value.Wrap(0));
        var innerConditional = new Conditional(greaterThanZero, Value.Wrap("positive"), Value.Wrap("zero"));
        var outerConditional = new Conditional(lessThanZero, Value.Wrap("negative"), innerConditional);

        // Act
        var expression = outerConditional.BuildExpression(context);
        var lambda = Expression.Lambda<Func<int, string>>(expression, param.BuildExpression(context));
        var compiled = lambda.Compile();

        // Assert
        await Assert.That(compiled(-5)).IsEqualTo("negative");
        await Assert.That(compiled(5)).IsEqualTo("positive");
        await Assert.That(compiled(0)).IsEqualTo("zero");
    }

    [Test]
    public async Task Conditional_GetTypeDefinition_ReturnsIfTrueType() {
        // Arrange
        var context = new InterpretationContext();
        var condition = Value.True;
        var ifTrue = Value.Wrap(42);
        var ifFalse = Value.Wrap(99);
        var conditional = new Conditional(condition, ifTrue, ifFalse);

        // Act
        var typeDef = conditional.GetTypeDefinition(context);

        // Assert
        await Assert.That(typeDef).IsNotNull();
        await Assert.That(typeDef.ReflectedType).IsEqualTo(typeof(int));
    }

    [Test]
    public async Task Conditional_ToString_ReturnsExpectedFormat() {
        // Arrange
        var condition = Value.True;
        var ifTrue = Value.Wrap(42);
        var ifFalse = Value.Wrap(0);
        var conditional = new Conditional(condition, ifTrue, ifFalse);

        // Act
        var result = conditional.ToString();

        // Assert
        await Assert.That(result).IsEqualTo("(True ? 42 : 0)");
    }

    [Test]
    public async Task Conditional_WithNullArguments_ThrowsArgumentNullException() {
        // Assert
        await Assert.That(() => new Conditional(null!, Value.Wrap(1), Value.Wrap(2)))
            .Throws<ArgumentNullException>();
        await Assert.That(() => new Conditional(Value.True, null!, Value.Wrap(2)))
            .Throws<ArgumentNullException>();
        await Assert.That(() => new Conditional(Value.True, Value.Wrap(1), null!))
            .Throws<ArgumentNullException>();
    }
}
