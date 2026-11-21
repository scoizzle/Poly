using Poly.Interpretation;
using Poly.Interpretation.CSharp;

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

    /// <summary>#907: <c>bool? ?? bool</c> is a <c>bool</c> in C#, and the VM must answer the same value.
    /// The word arm converted the right-hand word with <c>Expression.Convert(long, bool)</c>, which the
    /// expression tree refuses ("No coercion operator is defined between 'Int64' and 'Boolean'").</summary>
    [Test]
    public async Task Coalesce_NullableBool_AnswersTheCSharpValue() {
        var flag = new Parameter("flag", TypeReference.To<bool?>());
        var program = Interpreter.Compile(new Coalesce(flag, new Constant(true)));

        using (var exec = Interpreter.Execute(program, false))
            await Assert.That(exec.GetValue<bool>()).IsFalse();
        using (var exec = Interpreter.Execute(program, true))
            await Assert.That(exec.GetValue<bool>()).IsTrue();
        using (var exec = Interpreter.Execute(program, (object?)null))
            await Assert.That(exec.GetValue<bool>()).IsTrue();
    }

    /// <summary>#907: the same word arm for a CLR enum, whose frame word is its underlying number.
    /// The value is compared to the enum C# names, so the assertion is C#'s answer, not the word.</summary>
    [Test]
    public async Task Coalesce_NullableClrEnum_AnswersTheCSharpValue() {
        var day = new Parameter("day", TypeReference.To<DayOfWeek?>());
        var takesValue = Interpreter.Compile(
            new Equal(new Coalesce(day, new Constant(DayOfWeek.Friday)), new Constant(DayOfWeek.Tuesday)));
        using (var exec = Interpreter.Execute(takesValue, DayOfWeek.Tuesday))
            await Assert.That(exec.GetValue<bool>()).IsTrue();

        var takesFallback = Interpreter.Compile(
            new Equal(new Coalesce(day, new Constant(DayOfWeek.Friday)), new Constant(DayOfWeek.Friday)));
        using (var exec = Interpreter.Execute(takesFallback, (object?)null))
            await Assert.That(exec.GetValue<bool>()).IsTrue();
        using (var exec = Interpreter.Execute(takesFallback, DayOfWeek.Tuesday))
            await Assert.That(exec.GetValue<bool>()).IsFalse();
    }

    /// <summary>#907: C# is the oracle for the enum arm too — the printed coalesce is compiled by
    /// Roslyn and must answer the same Boolean the VM does. The arms are casts over literals, so the
    /// row needs no argument binding (the harness's reflection binding cannot carry an enum).</summary>
    [Test]
    public async Task Coalesce_NullableEnum_AgreesWithPrintedCSharp() {
        static Node Day(int value) => new TypeCast(new Constant(value), TypeReference.To<DayOfWeek>());
        static Node Optional(Node value) => new TypeCast(value, TypeReference.To<DayOfWeek?>());

        var present = AstParity.Compare("coalesce-enum-present",
            new Equal(new Coalesce(Optional(Day(5)), Day(1)), Day(5)));
        await Assert.That(present.Agrees).IsTrue().Because(present.Describe());

        var other = AstParity.Compare("coalesce-enum-other",
            new Equal(new Coalesce(Optional(Day(1)), Day(1)), Day(5)));
        await Assert.That(other.Agrees).IsTrue().Because(other.Describe());

        var absent = AstParity.Compare("coalesce-enum-absent",
            new Equal(new Coalesce(Optional(new Constant(null)), Day(1)), Day(1)));
        await Assert.That(absent.Agrees).IsTrue().Because(absent.Describe());
    }

    /// <summary>#907: the oracle is C# itself — the writer prints the coalesce, Roslyn compiles it, and
    /// the value must match the VM's for a present value and for an absent one. The left is a cast to
    /// <c>bool?</c> because a root parameter's reflection binding has no nullable form here.</summary>
    [Test]
    public async Task Coalesce_NullableBool_AgreesWithPrintedCSharp() {
        var x = new Parameter("x", TypeReference.To<bool>());
        var lambda = new Lambda([x],
            new Coalesce(new TypeCast(x, TypeReference.To<bool?>()), new Constant(true)));

        var present = AstParity.Compare("coalesce-bool-present", lambda, false);
        await Assert.That(present.Agrees).IsTrue().Because(present.Describe());
        var absent = AstParity.Compare("coalesce-bool-absent",
            new Lambda([new Parameter("y", TypeReference.To<bool?>()), x],
                new Coalesce(new Parameter("y", TypeReference.To<bool?>()), new Constant(true))),
            (object?)null, false);
        await Assert.That(absent.Agrees).IsTrue().Because(absent.Describe());
    }
}