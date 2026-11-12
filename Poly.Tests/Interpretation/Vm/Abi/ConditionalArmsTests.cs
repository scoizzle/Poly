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

    private static async Task AssertRejected(Node body, params object?[] args) {
        var thrown = Assert.Throws<InvalidOperationException>(() => {
            using var exec = Run(body, args);
        });
        await Assert.That(thrown.Message).Contains("VM compile rejected:").And.Contains("arms differ in representation");
    }

    [Test]
    public async Task Conditional_DecimalThenIntegral_Rejects() =>
        await AssertRejected(Pick(new Constant(1.5m), new Constant(2L)));

    [Test]
    public async Task Conditional_IntegralThenDecimal_Rejects() =>
        await AssertRejected(Pick(new Constant(2L), new Constant(1.5m)));

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
        await AssertRejected(Coalesced(TypeReference.To<decimal?>(), new Constant(2L)), (object?)null);

    [Test]
    public async Task Coalesce_ScalarNullableThenDecimal_Rejects() =>
        await AssertRejected(Coalesced(TypeReference.To<long?>(), new Constant(1.5m)), (object?)null);

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
    public async Task Switch_DecimalCaseIntegralDefault_Rejects() =>
        await AssertRejected(Switched(new Constant(1.5m), new Constant(2L)), 0L);

    [Test]
    public async Task Switch_IntegralCaseDecimalDefault_Rejects() =>
        await AssertRejected(Switched(new Constant(2L), new Constant(1.5m)), 0L);

    [Test]
    public async Task Switch_SameRepresentation_Runs() {
        using var exec = Run(Switched(new Constant(2L), new Constant(3L)), 0L);
        await Assert.That(exec.GetValue<long>()).IsEqualTo(3L);
    }

    /// <summary>A null first arm is not judgeable, but the arms after it are still compared with each other.</summary>
    [Test]
    public async Task Switch_NullArmFirst_StillRejectsTheMixedArmsAfterIt() =>
        await AssertRejected(
            new Block([new SwitchStatement(
                new Parameter("key", TypeReference.To<long>()),
                [new SwitchCase(new Constant(1L), new Constant(null)), new SwitchCase(new Constant(2L), new Constant(1.5m))],
                new Constant(2L))]),
            0L);

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

    [Test]
    [Arguments(1, "d", "1.5")]
    [Arguments(2, "n", "2")]
    public async Task StatementSwitch_BodiesAssignDifferentRepresentations_Runs(int key, string read, string expected) {
        var result = CSharpScript.Run(
            $"long k = {key}; decimal d = 0; long n = 0; switch (k) {{ case 1: d = 1.5m; break; default: n = 2; break; }} {read};");
        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Value).IsEqualTo(expected);
    }
}