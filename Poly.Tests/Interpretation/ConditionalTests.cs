using Poly.Interpretation;

namespace Poly.Tests.Interpretation;

public class ConditionalTests {
    [Test]
    public async Task Conditional_WithTrueCondition_ReturnsIfTrueValue() {
        using var exec = Interpreter.Execute(Interpreter.Compile(new Conditional(True, Wrap(42), Wrap(0))));

        await Assert.That(exec.GetValue<long>()).IsEqualTo(42L);
    }

    [Test]
    public async Task Conditional_WithFalseCondition_ReturnsIfFalseValue() {
        using var exec = Interpreter.Execute(Interpreter.Compile(new Conditional(False, Wrap(42), Wrap(99))));

        await Assert.That(exec.GetValue<long>()).IsEqualTo(99L);
    }

    [Test]
    public async Task Conditional_WithParameterCondition_EvaluatesCorrectly() {
        // Arrange
        var param = new Parameter("x", TypeReference.To<bool>());
        var node = new Conditional(param, Wrap(10), Wrap(20));

        // Act
        // Act
        var program = Interpreter.Compile(node);

        // Assert
        using (var exec = Interpreter.Execute(program, s => s.SetArgs(true)))
            await Assert.That(exec.GetValue<long>()).IsEqualTo(10L);
        using (var exec = Interpreter.Execute(program, s => s.SetArgs(false)))
            await Assert.That(exec.GetValue<long>()).IsEqualTo(20L);
    }

    [Test]
    public async Task Conditional_WithNestedConditionals_WorksCorrectly() {
        var inner = new Conditional(True, Wrap(5), Wrap(10));
        using var exec = Interpreter.Execute(Interpreter.Compile(new Conditional(False, Wrap(1), inner)));

        await Assert.That(exec.GetValue<long>()).IsEqualTo(5L);
    }

    [Test]
    public async Task Conditional_WithComparison_PicksBranch() {
        var param = new Parameter("x", TypeReference.To<int>());
        var node = new Conditional(new GreaterThan(param, Wrap(5)), Wrap(10), Wrap(0));
        // Act
        var program = Interpreter.Compile(node);
        using (var exec = Interpreter.Execute(program, s => s.SetArgs(10)))
            await Assert.That(exec.GetValue<long>()).IsEqualTo(10L);
        using (var exec = Interpreter.Execute(program, s => s.SetArgs(3)))
            await Assert.That(exec.GetValue<long>()).IsEqualTo(0L);
    }
}