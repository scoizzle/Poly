using Poly.Interpretation;

namespace Poly.Tests.Interpretation;

public class CoalesceTests {
    [Test]
    public async Task Coalesce_WithNullLeft_ReturnsRightValue() {
        // Arrange
        var node = new Coalesce(Wrap(null as int?), Wrap(42));

        // Act
        using var exec = Interpreter.Execute(Interpreter.Compile(node));

        // Assert
        await Assert.That(exec.GetValue<long>()).IsEqualTo(42L);
    }

    [Test]
    public async Task Coalesce_WithNonNullLeft_ReturnsLeftValue() {
        // Arrange
        var node = new Coalesce(Wrap(100 as int?), Wrap(42));

        // Act
        using var exec = Interpreter.Execute(Interpreter.Compile(node));

        // Assert
        await Assert.That(exec.GetValue<long>()).IsEqualTo(100L);
    }

    [Test]
    public async Task Coalesce_WithParameterLeft_EvaluatesCorrectly() {
        // Arrange
        var param = new Parameter("x", TypeReference.To<int?>());
        var node = new Coalesce(param, Wrap(99));

        // Act
        var program = Interpreter.Compile(node);

        // Assert
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
        using var exec = Interpreter.Execute(Interpreter.Compile(node));

        // Assert
        await Assert.That(exec.GetValue<long>()).IsEqualTo(10L);
    }

    [Test]
    public async Task Coalesce_WithObjects_WorksCorrectly() {
        // Arrange
        var node = new Coalesce(Wrap(null as string), Wrap("default"));

        // Act
        using var exec = Interpreter.Execute(Interpreter.Compile(node));

        // Assert
        await Assert.That(exec.GetValue<string>()).IsEqualTo("default");
    }

    [Test]
    public async Task Coalesce_NullableZero_IsValueNotNull() {
        var param = new Parameter("x", TypeReference.To<int?>());
        var node = new Coalesce(param, Wrap(100));
        var program = Interpreter.Compile(node);
        using var exec = Interpreter.Execute(program, s => s.SetArgs(0));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(0L);
    }

    [Test]
    public async Task Coalesce_WithNullArguments_ThrowsArgumentNullException() {
        await Assert.That(() => new Coalesce(null!, Wrap(42))).Throws<ArgumentNullException>();
    }
}