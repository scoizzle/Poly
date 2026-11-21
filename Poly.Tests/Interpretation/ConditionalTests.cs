using Poly.Interpretation;
using Poly.Interpretation.CSharp;

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

    // #803: a conditional whose arms are one nullable and one non-nullable value of the
    // same underlying type. C# lifts the non-nullable arm, so the result is the nullable
    // type and a null arm yields null.
    [Test]
    public async Task Conditional_NullableThenNumber_NullTaken_YieldsNull() {
        var flag = new Parameter("flag", TypeReference.To<bool>());
        var on = new Parameter("on", TypeReference.To<long?>());
        var node = new Conditional(flag, on, new Constant(5L));
        var program = Interpreter.Compile(node);

        using var taken = Interpreter.Execute(program, s => s.SetArgs(true, null));
        await Assert.That(taken.GetValue<long?>()).IsNull();
    }

    [Test]
    public async Task Conditional_NullableThenNumber_ValueTaken_YieldsTheValue() {
        var flag = new Parameter("flag", TypeReference.To<bool>());
        var on = new Parameter("on", TypeReference.To<long?>());
        var program = Interpreter.Compile(new Conditional(flag, on, new Constant(5L)));

        using var taken = Interpreter.Execute(program, s => s.SetArgs(true, 7L));
        await Assert.That(taken.GetValue<long?>()).IsEqualTo(7L);
        using var other = Interpreter.Execute(program, s => s.SetArgs(false, null));
        await Assert.That(other.GetValue<long?>()).IsEqualTo(5L);
    }

    [Test]
    public async Task Conditional_NumberThenNullable_NullTaken_YieldsNull() {
        var flag = new Parameter("flag", TypeReference.To<bool>());
        var on = new Parameter("on", TypeReference.To<long?>());
        var program = Interpreter.Compile(new Conditional(flag, new Constant(5L), on));

        using var taken = Interpreter.Execute(program, s => s.SetArgs(false, null));
        await Assert.That(taken.GetValue<long?>()).IsNull();
    }

    [Test]
    public async Task Conditional_NullableDecimalThenDecimal_AgreesWithCSharp() {
        var flag = new Parameter("flag", TypeReference.To<bool>());
        var od = new Parameter("od", TypeReference.To<decimal?>());
        var program = Interpreter.Compile(new Conditional(flag, od, new Constant(5m)));

        using var absent = Interpreter.Execute(program, s => s.SetArgs(true, null));
        await Assert.That(absent.GetValue<decimal?>()).IsNull();
        using var present = Interpreter.Execute(program, s => s.SetArgs(true, 0.1m));
        await Assert.That(present.GetValue<decimal?>()).IsEqualTo(0.1m);
        using var other = Interpreter.Execute(program, s => s.SetArgs(false, null));
        await Assert.That(other.GetValue<decimal?>()).IsEqualTo(5m);
    }
}