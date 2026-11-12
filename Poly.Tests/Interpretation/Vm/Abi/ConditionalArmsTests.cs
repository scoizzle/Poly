using Poly.Interpretation;
using Poly.Interpretation.CSharp;
using Poly.Introspection;

namespace Poly.Tests.Interpretation.Vm.Abi;

/// <summary>
/// A conditional whose arms differ in representation (an integral scalar word against a heap handle such
/// as a Decimal) must not compile: the merged word would be read as one or the other (#605). Same-representation
/// pairs, and a null arm, stay valid.
/// </summary>
public class ConditionalArmsTests {
    /// <summary>The condition is a parameter: a constant condition folds to one arm and never reaches the emitter.</summary>
    private static Node Pick(Node ifTrue, Node ifFalse) =>
        new Block([new Conditional(new Parameter("pickFirst", TypeReference.To<bool>()), ifTrue, ifFalse)]);

    private static ExecutionResult Run(Node body, params object?[] args) =>
        Interpreter.Execute(Interpreter.Compile(body), s => s.SetArgs(args.Length == 0 ? [false] : args));

    private static async Task AssertRejected(Node body, string construct, params object?[] args) {
        var thrown = Assert.Throws<InvalidOperationException>(() => {
            using var exec = Run(body, args);
        });
        await Assert.That(thrown.Message).Contains($"VM compile rejected: {construct} arms differ in representation");
    }

    [Test]
    public async Task Conditional_DecimalThenIntegral_Rejects() =>
        await AssertRejected(Pick(new Constant(1.5m), new Constant(2L)), "conditional");

    [Test]
    public async Task Conditional_IntegralThenDecimal_Rejects() =>
        await AssertRejected(Pick(new Constant(2L), new Constant(1.5m)), "conditional");

    [Test]
    public async Task Conditional_IntegralPair_Runs() {
        using var exec = Run(Pick(new Constant(2L), new Constant(3L)));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(3L);
    }

    [Test]
    public async Task Conditional_DecimalPair_Runs() {
        using var exec = Run(Pick(new Constant(1.5m), new Constant(2.5m)));
        await Assert.That(exec.GetValue<decimal>()).IsEqualTo(2.5m);
    }

    [Test]
    public async Task Conditional_StringPair_Runs() {
        using var exec = Run(Pick(new Constant("a"), new Constant("b")));
        await Assert.That(exec.GetValue<string>()).IsEqualTo("b");
    }

    [Test]
    public async Task Conditional_NullArmAgainstReference_Runs() {
        using var exec = Run(Pick(new Constant("a"), new Constant(null)));
        await Assert.That(exec.GetValue<string?>()).IsNull();
    }

    // Every other site that merges two arm values into one word (A11): a coalesce and a switch.

    private static Node Coalesced(TypeReference leftType, Node right) =>
        new Block([new Coalesce(new Parameter("left", leftType), right)]);

    [Test]
    public async Task Coalesce_HeapNullableDecimalThenIntegral_Rejects() =>
        await AssertRejected(Coalesced(TypeReference.To<decimal?>(), new Constant(2L)), "coalesce", (object?)null);

    [Test]
    public async Task Coalesce_ScalarNullableThenDecimal_Rejects() =>
        await AssertRejected(Coalesced(TypeReference.To<long?>(), new Constant(1.5m)), "coalesce", (object?)null);

    [Test]
    public async Task Coalesce_SameRepresentation_Runs() {
        using var exec = Run(Coalesced(TypeReference.To<long?>(), new Constant(2L)), (object?)null);
        await Assert.That(exec.GetValue<long>()).IsEqualTo(2L);
    }

    private static Node Switched(Node caseBody, Node defaultBody) =>
        new Block([new SwitchStatement(
            new Parameter("key", TypeReference.To<long>()),
            [new SwitchCase(new Constant(1L), caseBody)],
            defaultBody)]);

    [Test]
    public async Task Switch_SameRepresentation_Runs() {
        using var exec = Run(Switched(new Constant(2L), new Constant(3L)), 0L);
        await Assert.That(exec.GetValue<long>()).IsEqualTo(3L);
    }

    // Legal code that ran correctly before the guard must keep running (a nullable operand is judged by its
    // underlying type; a statement switch's discarded bodies are not a merged value).

    [Test]
    [Arguments(null, 5L, 5L)]
    [Arguments(3L, 5L, 3L)]
    [Arguments(null, null, 0L)]
    public async Task NestedCoalesce_NullableLong_Runs(long? first, long? second, long expected) {
        var t = TypeReference.To<long?>();
        var body = new Block([new Coalesce(new Coalesce(new Parameter("p1", t), new Parameter("p2", t)), new Constant(0L))]);
        using var exec = Run(body, first, second);
        await Assert.That(exec.GetValue<long>()).IsEqualTo(expected);
    }

    [Test]
    [Arguments(true, 3L, 3L)]
    [Arguments(false, 3L, 5L)]
    public async Task Conditional_NullableLongArmFirst_Runs(bool flag, long value, long expected) {
        var body = new Block([new Conditional(
            new Parameter("flag", TypeReference.To<bool>()), new Parameter("p", TypeReference.To<long?>()), new Constant(5L))]);
        using var exec = Run(body, flag, value);
        await Assert.That(exec.GetValue<long?>()).IsEqualTo(expected);
    }

    [Test]
    [Arguments(true, 3L, 5L)]
    [Arguments(false, 3L, 3L)]
    public async Task Conditional_NullableLongArmSecond_Runs(bool flag, long value, long expected) {
        var body = new Block([new Conditional(
            new Parameter("flag", TypeReference.To<bool>()), new Constant(5L), new Parameter("p", TypeReference.To<long?>()))]);
        using var exec = Run(body, flag, value);
        await Assert.That(exec.GetValue<long?>()).IsEqualTo(expected);
    }

    /// <summary>A statement switch is legal whatever its bodies do: assignments, blocks or call statements of
    /// different representations. It has no value, so nothing is merged; a switch whose value is read is T-21's.</summary>
    [Test]
    [Arguments("long k = 1; decimal d = 0; long n = 0; switch (k) { case 1: d = 1.5m; break; default: n = 2; break; } d;", "1.5")]
    [Arguments("long k = 2; decimal d = 0; long n = 0; switch (k) { case 1: d = 1.5m; break; default: n = 2; break; } n;", "2")]
    [Arguments("long k = 1; decimal d = 0; long n = 0; switch (k) { case 1: { d = 1.5m; } break; default: { n = 2; } break; } d;", "1.5")]
    [Arguments("long k = 2; decimal d = 0; long n = 0; switch (k) { case 1: { d = 1.5m; } break; default: { n = 2; } break; } n;", "2")]
    [Arguments("class C { public C() { } decimal A() { return 1.5m; } long B() { return 2; } long M(long k) { switch (k) { case 1: this.A(); break; default: this.B(); break; } return k; } } new C().M(1);", "1")]
    [Arguments("class C { public C() { } decimal A() { return 1.5m; } long B() { return 2; } long M(long k) { switch (k) { case 1: this.A(); break; default: this.B(); break; } return k; } } new C().M(2);", "2")]
    public async Task StatementSwitch_BodiesOfDifferentRepresentations_Runs(string code, string expected) {
        var result = CSharpScript.Run(code);
        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Value).IsEqualTo(expected);
    }

    [Test]
    [Arguments(null, 5, 5)]
    [Arguments(3, 5, 3)]
    [Arguments(null, null, 0)]
    public async Task NestedCoalesce_NullableDecimal_Runs(int? first, int? second, int expected) {
        var t = TypeReference.To<decimal?>();
        var body = new Block([new Coalesce(new Coalesce(new Parameter("p1", t), new Parameter("p2", t)), new Constant(0m))]);
        using var exec = Run(body, first is null ? null : (decimal)first, second is null ? null : (decimal)second);
        await Assert.That(exec.GetValue<decimal>()).IsEqualTo((decimal)expected);
    }

    /// <summary>Characterization of a known residual (#803, T-06): a nullable arm that is null when taken has no
    /// value to merge and the VM throws today. Pinned so the row that fixes it flips this test.</summary>
    [Test]
    public async Task Conditional_NullableArmTakenWhileNull_ThrowsToday() {
        var body = new Block([new Conditional(
            new Parameter("flag", TypeReference.To<bool>()), new Parameter("p", TypeReference.To<long?>()), new Constant(5L))]);
        var thrown = Assert.Throws<InvalidOperationException>(() => {
            using var exec = Run(body, true, (object?)null);
            _ = exec.GetValue<long?>();
        });
        await Assert.That(thrown.Message).Contains("Nullable object must have a value");
    }

    // The predicate's edges: only a scalar against a heap handle is rejected.

    [Test]
    public async Task Conditional_IntAgainstLong_Runs() {
        using var exec = Run(Pick(new Constant(1), new Constant(2L)));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(2L);
    }

    [Test]
    public async Task Conditional_BoolPair_Runs() {
        using var exec = Run(Pick(new Constant(true), new Constant(false)));
        await Assert.That(exec.GetValue<bool>()).IsFalse();
    }

    [Test]
    public async Task Conditional_DateTimePair_Runs() {
        using var exec = Run(Pick(new Constant(new DateTime(2020, 1, 1)), new Constant(new DateTime(2021, 2, 2))));
        await Assert.That(exec.GetValue<DateTime>()).IsEqualTo(new DateTime(2021, 2, 2));
    }

    /// <summary>Two different heap types are not a scalar-vs-handle pair: the guard does not judge them and each
    /// arm reads back as itself (the conversion between them is T-09's).</summary>
    [Test]
    [Arguments(true, true)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(false, false)]
    public async Task Conditional_DecimalAgainstText_EachArmReadsBackAsItself(bool flag, bool decimalFirst) {
        var body = decimalFirst
            ? Pick(new Constant(1.5m), new Constant("a"))
            : Pick(new Constant("a"), new Constant(1.5m));
        using var exec = Run(body, flag);
        var takesDecimal = flag == decimalFirst;
        if (takesDecimal)
            await Assert.That(exec.GetValue<decimal>()).IsEqualTo(1.5m);
        else
            await Assert.That(exec.GetValue<string>()).IsEqualTo("a");
    }

    [Test]
    [Arguments(true, true, 1L)]
    [Arguments(false, true, 2L)]
    [Arguments(true, false, 2L)]
    [Arguments(false, false, 1L)]
    public async Task Conditional_EnumAgainstItsUnderlyingValue_ReadsBackTheTakenArm(bool flag, bool enumFirst, long expected) {
        var body = enumFirst
            ? Pick(new Constant(DayOfWeek.Monday), new Constant(2L))
            : Pick(new Constant(2L), new Constant(DayOfWeek.Monday));
        using var exec = Run(body, flag);
        await Assert.That(exec.GetValue<long>()).IsEqualTo(expected);
    }
}