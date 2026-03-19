using Poly.Interpretation.AbstractSyntaxTree;
using Poly.Interpretation.AbstractSyntaxTree.Arithmetic;
using Poly.Interpretation.AbstractSyntaxTree.Boolean;
using Poly.Interpretation.AbstractSyntaxTree.Comparison;
using Poly.Interpretation.AbstractSyntaxTree.Equality;
using Poly.Interpretation.Analysis;
using Poly.Interpretation.Analysis.ConstantFolding;

using static Poly.Interpretation.AbstractSyntaxTree.NodeExtensions;

namespace Poly.Tests.Interpretation;

public class ConstantFoldingTests {
    [Test]
    public async Task AddConstants_FoldsToSum()
    {
        // Arrange: 1 + 2
        var ast = new Add(Wrap(1), Wrap(2));

        var analyzer = new AnalyzerBuilder()
            .UseConstantFolding()
            .Build();

        // Act
        var result = analyzer.Analyze(ast);

        // Assert
        await Assert.That(result.IsConstant(ast)).IsTrue();
        await Assert.That(result.GetConstantValue(ast)).IsEqualTo(3);
    }

    [Test]
    public async Task SubtractConstants_FoldsToDifference()
    {
        // Arrange: 10 - 3
        var ast = new Subtract(Wrap(10), Wrap(3));

        var analyzer = new AnalyzerBuilder()
            .UseConstantFolding()
            .Build();

        // Act
        var result = analyzer.Analyze(ast);

        // Assert
        await Assert.That(result.IsConstant(ast)).IsTrue();
        await Assert.That(result.GetConstantValue(ast)).IsEqualTo(7);
    }

    [Test]
    public async Task MultiplyConstants_FoldsToProduct()
    {
        // Arrange: 4 * 5
        var ast = new Multiply(Wrap(4), Wrap(5));

        var analyzer = new AnalyzerBuilder()
            .UseConstantFolding()
            .Build();

        // Act
        var result = analyzer.Analyze(ast);

        // Assert
        await Assert.That(result.IsConstant(ast)).IsTrue();
        await Assert.That(result.GetConstantValue(ast)).IsEqualTo(20);
    }

    [Test]
    public async Task DivideConstants_FoldsToQuotient()
    {
        // Arrange: 20 / 4
        var ast = new Divide(Wrap(20), Wrap(4));

        var analyzer = new AnalyzerBuilder()
            .UseConstantFolding()
            .Build();

        // Act
        var result = analyzer.Analyze(ast);

        // Assert
        await Assert.That(result.IsConstant(ast)).IsTrue();
        await Assert.That(result.GetConstantValue(ast)).IsEqualTo(5);
    }

    [Test]
    public async Task ModuloConstants_FoldsToRemainder()
    {
        // Arrange: 17 % 5
        var ast = new Modulo(Wrap(17), Wrap(5));

        var analyzer = new AnalyzerBuilder()
            .UseConstantFolding()
            .Build();

        // Act
        var result = analyzer.Analyze(ast);

        // Assert
        await Assert.That(result.IsConstant(ast)).IsTrue();
        await Assert.That(result.GetConstantValue(ast)).IsEqualTo(2);
    }

    [Test]
    public async Task UnaryMinus_FoldsToNegation()
    {
        // Arrange: -42
        var ast = new UnaryMinus(Wrap(42));

        var analyzer = new AnalyzerBuilder()
            .UseConstantFolding()
            .Build();

        // Act
        var result = analyzer.Analyze(ast);

        // Assert
        await Assert.That(result.IsConstant(ast)).IsTrue();
        await Assert.That(result.GetConstantValue(ast)).IsEqualTo(-42);
    }

    [Test]
    public async Task NestedArithmetic_FoldsRecursively()
    {
        // Arrange: (1 + 2) * (3 + 4) = 3 * 7 = 21
        var left = new Add(Wrap(1), Wrap(2));
        var right = new Add(Wrap(3), Wrap(4));
        var ast = new Multiply(left, right);

        var analyzer = new AnalyzerBuilder()
            .UseConstantFolding()
            .Build();

        // Act
        var result = analyzer.Analyze(ast);

        // Assert
        await Assert.That(result.IsConstant(left)).IsTrue();
        await Assert.That(result.GetConstantValue(left)).IsEqualTo(3);
        await Assert.That(result.IsConstant(right)).IsTrue();
        await Assert.That(result.GetConstantValue(right)).IsEqualTo(7);
        await Assert.That(result.IsConstant(ast)).IsTrue();
        await Assert.That(result.GetConstantValue(ast)).IsEqualTo(21);
    }

    [Test]
    public async Task AndBoolean_FoldsCorrectly()
    {
        // Arrange: true && false
        var ast = new And(Wrap(true), Wrap(false));

        var analyzer = new AnalyzerBuilder()
            .UseConstantFolding()
            .Build();

        // Act
        var result = analyzer.Analyze(ast);

        // Assert
        await Assert.That(result.IsConstant(ast)).IsTrue();
        await Assert.That((bool?)result.GetConstantValue(ast)).IsFalse();
    }

    [Test]
    public async Task OrBoolean_FoldsCorrectly()
    {
        // Arrange: true || false
        var ast = new Or(Wrap(true), Wrap(false));

        var analyzer = new AnalyzerBuilder()
            .UseConstantFolding()
            .Build();

        // Act
        var result = analyzer.Analyze(ast);

        // Assert
        await Assert.That(result.IsConstant(ast)).IsTrue();
        await Assert.That((bool?)result.GetConstantValue(ast)).IsTrue();
    }

    [Test]
    public async Task NotBoolean_FoldsCorrectly()
    {
        // Arrange: !true
        var ast = new Not(Wrap(true));

        var analyzer = new AnalyzerBuilder()
            .UseConstantFolding()
            .Build();

        // Act
        var result = analyzer.Analyze(ast);

        // Assert
        await Assert.That(result.IsConstant(ast)).IsTrue();
        await Assert.That((bool?)result.GetConstantValue(ast)).IsFalse();
    }

    [Test]
    public async Task GreaterThan_FoldsCorrectly()
    {
        // Arrange: 5 > 3
        var ast = new GreaterThan(Wrap(5), Wrap(3));

        var analyzer = new AnalyzerBuilder()
            .UseConstantFolding()
            .Build();

        // Act
        var result = analyzer.Analyze(ast);

        // Assert
        await Assert.That(result.IsConstant(ast)).IsTrue();
        await Assert.That((bool?)result.GetConstantValue(ast)).IsTrue();
    }

    [Test]
    public async Task LessThanOrEqual_FoldsCorrectly()
    {
        // Arrange: 3 <= 3
        var ast = new LessThanOrEqual(Wrap(3), Wrap(3));

        var analyzer = new AnalyzerBuilder()
            .UseConstantFolding()
            .Build();

        // Act
        var result = analyzer.Analyze(ast);

        // Assert
        await Assert.That(result.IsConstant(ast)).IsTrue();
        await Assert.That((bool?)result.GetConstantValue(ast)).IsTrue();
    }

    [Test]
    public async Task Equal_FoldsCorrectly()
    {
        // Arrange: 42 == 42
        var ast = new Equal(Wrap(42), Wrap(42));

        var analyzer = new AnalyzerBuilder()
            .UseConstantFolding()
            .Build();

        // Act
        var result = analyzer.Analyze(ast);

        // Assert
        await Assert.That(result.IsConstant(ast)).IsTrue();
        await Assert.That((bool?)result.GetConstantValue(ast)).IsTrue();
    }

    [Test]
    public async Task NotEqual_FoldsCorrectly()
    {
        // Arrange: 42 != 43
        var ast = new NotEqual(Wrap(42), Wrap(43));

        var analyzer = new AnalyzerBuilder()
            .UseConstantFolding()
            .Build();

        // Act
        var result = analyzer.Analyze(ast);

        // Assert
        await Assert.That(result.IsConstant(ast)).IsTrue();
        await Assert.That((bool?)result.GetConstantValue(ast)).IsTrue();
    }

    [Test]
    public async Task ConditionalWithTrueCondition_FoldsToThenBranch()
    {
        // Arrange: true ? 1 : 2
        var ast = new Conditional(Wrap(true), Wrap(1), Wrap(2));

        var analyzer = new AnalyzerBuilder()
            .UseConstantFolding()
            .Build();

        // Act
        var result = analyzer.Analyze(ast);

        // Assert
        await Assert.That(result.IsConstant(ast)).IsTrue();
        await Assert.That(result.GetConstantValue(ast)).IsEqualTo(1);
    }

    [Test]
    public async Task ConditionalWithFalseCondition_FoldsToElseBranch()
    {
        // Arrange: false ? 1 : 2
        var ast = new Conditional(Wrap(false), Wrap(1), Wrap(2));

        var analyzer = new AnalyzerBuilder()
            .UseConstantFolding()
            .Build();

        // Act
        var result = analyzer.Analyze(ast);

        // Assert
        await Assert.That(result.IsConstant(ast)).IsTrue();
        await Assert.That(result.GetConstantValue(ast)).IsEqualTo(2);
    }

    [Test]
    public async Task NonConstantExpression_DoesNotFold()
    {
        // Arrange: x + 1 (where x is a variable)
        var variable = new Variable("x");
        var ast = new Add(variable, Wrap(1));

        var analyzer = new AnalyzerBuilder()
            .UseConstantFolding()
            .Build();

        // Act
        var result = analyzer.Analyze(ast);

        // Assert
        await Assert.That(result.IsConstant(ast)).IsFalse();
        await Assert.That(result.IsConstant(variable)).IsFalse();
    }

    [Test]
    public async Task DivisionByZero_DoesNotFold()
    {
        // Arrange: 10 / 0
        var ast = new Divide(Wrap(10), Wrap(0));

        var analyzer = new AnalyzerBuilder()
            .UseConstantFolding()
            .Build();

        // Act
        var result = analyzer.Analyze(ast);

        // Assert - division by zero should not fold
        await Assert.That(result.IsConstant(ast)).IsFalse();
    }

    [Test]
    public async Task FloatingPointArithmetic_FoldsCorrectly()
    {
        // Arrange: 3.5 + 2.5
        var ast = new Add(Wrap(3.5), Wrap(2.5));

        var analyzer = new AnalyzerBuilder()
            .UseConstantFolding()
            .Build();

        // Act
        var result = analyzer.Analyze(ast);

        // Assert
        await Assert.That(result.IsConstant(ast)).IsTrue();
        await Assert.That(result.GetConstantValue(ast)).IsEqualTo(6.0);
    }

    [Test]
    public async Task StringConcatenation_FoldsCorrectly()
    {
        // Arrange: "Hello" + " World"
        var ast = new Add(Wrap("Hello"), Wrap(" World"));

        var analyzer = new AnalyzerBuilder()
            .UseConstantFolding()
            .Build();

        // Act
        var result = analyzer.Analyze(ast);

        // Assert
        await Assert.That(result.IsConstant(ast)).IsTrue();
        await Assert.That(result.GetConstantValue(ast)).IsEqualTo("Hello World");
    }

    [Test]
    public async Task Coalesce_WithNonNullLeft_FoldsToLeft()
    {
        // Arrange: "value" ?? "default"
        var ast = new Coalesce(Wrap("value"), Wrap("default"));

        var analyzer = new AnalyzerBuilder()
            .UseConstantFolding()
            .Build();

        // Act
        var result = analyzer.Analyze(ast);

        // Assert
        await Assert.That(result.IsConstant(ast)).IsTrue();
        await Assert.That(result.GetConstantValue(ast)).IsEqualTo("value");
    }

    [Test]
    public async Task ComplexExpression_FoldsCompletely()
    {
        // Arrange: ((2 + 3) * 4 - 5) / 3 = (5 * 4 - 5) / 3 = (20 - 5) / 3 = 15 / 3 = 5
        var add = new Add(Wrap(2), Wrap(3));
        var mul = new Multiply(add, Wrap(4));
        var sub = new Subtract(mul, Wrap(5));
        var ast = new Divide(sub, Wrap(3));

        var analyzer = new AnalyzerBuilder()
            .UseConstantFolding()
            .Build();

        // Act
        var result = analyzer.Analyze(ast);

        // Assert
        await Assert.That(result.IsConstant(ast)).IsTrue();
        await Assert.That(result.GetConstantValue(ast)).IsEqualTo(5);
    }
}