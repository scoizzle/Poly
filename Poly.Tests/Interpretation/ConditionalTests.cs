using Poly.Interpretation;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.Interpretation;

public class ConditionalTests {
    [Test]
    public async Task Conditional_WithTrueCondition_ReturnsIfTrueValue() {
        await new Conditional(True, Wrap(42), Wrap(0)).AssertDualOracleInt(42);
    }

    [Test]
    public async Task Conditional_WithFalseCondition_ReturnsIfFalseValue() {
        await new Conditional(False, Wrap(42), Wrap(99)).AssertDualOracleInt(99);
    }

    [Test]
    public async Task Conditional_WithParameterCondition_EvaluatesCorrectly() {
        // Arrange
        var param = new Parameter("x", TypeReference.To<bool>());
        var node = new Conditional(param, Wrap(10), Wrap(20));

        // Act
        var compiled = node.CompileLambda<Func<bool, int>>((param, typeof(bool)));

        // Assert
        await Assert.That(compiled(true)).IsEqualTo(10);
        await Assert.That(compiled(false)).IsEqualTo(20);

        var program = Interpreter.Compile(node);
        using (var exec = Interpreter.Execute(program, s => s.SetArgs(true)))
            await Assert.That(exec.GetValue<long>()).IsEqualTo(10L);
        using (var exec = Interpreter.Execute(program, s => s.SetArgs(false)))
            await Assert.That(exec.GetValue<long>()).IsEqualTo(20L);
    }

    [Test]
    public async Task Conditional_WithNestedConditionals_WorksCorrectly() {
        var inner = new Conditional(True, Wrap(5), Wrap(10));
        await new Conditional(False, Wrap(1), inner).AssertDualOracleInt(5);
    }

    [Test]
    public async Task Conditional_WithComparison_PicksBranch() {
        var param = new Parameter("x", TypeReference.To<int>());
        var node = new Conditional(new GreaterThan(param, Wrap(5)), Wrap(10), Wrap(0));
        var compiled = node.CompileLambda<Func<int, int>>((param, typeof(int)));
        await Assert.That(compiled(10)).IsEqualTo(10);
        await Assert.That(compiled(3)).IsEqualTo(0);
        var program = Interpreter.Compile(node);
        using (var exec = Interpreter.Execute(program, s => s.SetArgs(10)))
            await Assert.That(exec.GetValue<long>()).IsEqualTo(10L);
        using (var exec = Interpreter.Execute(program, s => s.SetArgs(3)))
            await Assert.That(exec.GetValue<long>()).IsEqualTo(0L);
    }
}