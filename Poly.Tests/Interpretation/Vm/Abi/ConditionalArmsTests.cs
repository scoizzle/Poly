using Poly.Interpretation;
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

    private static ExecutionResult Run(Node body) =>
        Interpreter.Execute(Interpreter.Compile(body), s => s.SetArgs(false));

    private static async Task AssertRejected(Node body) {
        var thrown = Assert.Throws<InvalidOperationException>(() => {
            using var exec = Run(body);
        });
        await Assert.That(thrown.Message).Contains("VM compile rejected: conditional arms differ in representation");
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
}