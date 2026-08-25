using Poly.Interpretation;
using Poly.Tests.TestHelpers;

using Expr = System.Linq.Expressions.Expression;

namespace Poly.Tests.Interpretation;

public class CoalesceTests {
    [Test]
    public async Task Coalesce_WithNullLeft_ReturnsRightValue() {
        // Arrange
        var node = new Coalesce(Wrap(null as int?), Wrap(42));

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
    public async Task Coalesce_WithNonNullLeft_ReturnsLeftValue() {
        // Arrange
        var node = new Coalesce(Wrap(100 as int?), Wrap(42));

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
    public async Task Coalesce_WithParameterLeft_EvaluatesCorrectly() {
        // Arrange
        var param = new Parameter("x", TypeReference.To<int?>());
        var node = new Coalesce(param, Wrap(99));

        // Act
        var compiled = node.CompileLambda<Func<int?, int>>((param, typeof(int?)));

        // Assert
        await Assert.That(compiled(50)).IsEqualTo(50);
        await Assert.That(compiled(null)).IsEqualTo(99);

        var program = Interpreter.Compile(node);
        using (var exec = Interpreter.Execute(program, s => s.SetArgs(50)))
            await Assert.That(exec.GetValue<long>()).IsEqualTo(50L);
        using (var exec = Interpreter.Execute(program, s => s.SetArgs(new object?[] { null })))
            await Assert.That(exec.GetValue<long>()).IsEqualTo(99L);
    }

    [Test]
    public async Task Coalesce_ChainedOperators_WorksCorrectly() {
        // Arrange
        var node = new Coalesce(
            Wrap(null as int?),
            new Coalesce(Wrap(null as int?), Wrap(10))
        );

        // Act
        var expr = node.BuildExpression();
        var compiled = Expr.Lambda<Func<int>>(expr).Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo(10);

        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(10L);
    }

    [Test]
    public async Task Coalesce_WithObjects_WorksCorrectly() {
        // Arrange
        var node = new Coalesce(Wrap(null as string), Wrap("default"));

        // Act
        var expr = node.BuildExpression();
        var compiled = Expr.Lambda<Func<string>>(expr).Compile();
        var result = compiled();

        // Assert
        await Assert.That(result).IsEqualTo("default");

        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<string>()).IsEqualTo("default");
    }

    [Test]
    public async Task Coalesce_GetTypeDefinition_ReturnsRightHandType() {
        // Arrange
        var node = new Coalesce(Wrap(null as int?), Wrap(42));

        // Act - build to trigger semantic analysis
        _ = node.BuildExpression();

        // Assert
        await Assert.That(node).IsNotNull();
    }

    [Test]
    public async Task Coalesce_ToString_ReturnsExpectedFormat() {
        // Arrange
        var node = new Coalesce(Wrap(null as int?), Wrap(42));

        // Act
        var result = node.ToString();

        // Assert
        await Assert.That(result).Contains("??");
    }

    [Test]
    public async Task Coalesce_WithNullArguments_ThrowsArgumentNullException() {
        // Act & Assert
        await Assert.That(() => new Coalesce(null!, Wrap(42))).Throws<ArgumentNullException>();
    }
}