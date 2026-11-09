using Poly.Interpretation;

namespace Poly.Tests.Interpretation;

/// <summary>
/// #374: string concatenation shapes that stack-overflowed the VM compiler (AbiEmitter) and killed the
/// process. A concatenation lowers to an Invoke; AvailableExpressionPass rewrote a repeated one into a
/// store the emitter followed back into the same Invoke forever. Each test asserts the VALUE through the
/// Roslyn differential: the VM and the printed C# both produce it.
/// </summary>
public class StringConcatOverflowTests {
    // s1 = "a", s2 = "b", b1 = true
    static Node Program(Func<Variable, Variable, Variable, Node> body) {
        var s1 = new Variable("s1");
        var s2 = new Variable("s2");
        var b1 = new Variable("b1");
        return new Block(
            [
                new Assignment(s1, new Constant("a")),
                new Assignment(s2, new Constant("b")),
                new Assignment(b1, new Constant(true)),
                body(s1, s2, b1),
            ],
            [s1, s2, b1]);
    }

    static Node Cat(Node l, Node r) => new Add(l, r);

    static async Task AssertAgreesWith(string label, Node program, string expected) {
        var row = AstParity.Compare(label, program);
        await Assert.That(row.Agrees).IsTrue().Because(row.Describe());
        await Assert.That(row.Vm).IsEqualTo(ParityDisposal.Executed);
        await Assert.That(row.VmOutcome).IsEqualTo(expected);
        await Assert.That(row.PrintedOutcome).IsEqualTo(expected);
    }

    [Test]
    public Task NotEqual_OfTwoIdenticalConcatenations_IsFalse() =>
        AssertAgreesWith("ne-cat-cat",
            Program((s1, _, _) => new NotEqual(Cat(s1, new Constant("c")), Cat(s1, new Constant("c")))),
            "bool:false");

    [Test]
    public Task Equal_OfTwoIdenticalConcatenations_IsTrue() =>
        AssertAgreesWith("eq-cat-cat",
            Program((s1, _, _) => new Equal(Cat(s1, new Constant("c")), Cat(s1, new Constant("c")))),
            "bool:true");

    [Test]
    public Task Conditional_WithTheSameConcatenationInBothBranches_YieldsIt() =>
        AssertAgreesWith("cond-cat-cat",
            Program((s1, s2, b1) => new Conditional(b1, Cat(s1, s2), Cat(s1, s2))),
            "string:ab");

    [Test]
    public Task Concatenation_OfTwoIdenticalConcatenations_Doubles() =>
        AssertAgreesWith("cat-of-cats",
            Program((s1, s2, _) => Cat(Cat(s1, s2), Cat(s1, s2))),
            "string:abab");

    [Test]
    public Task Concatenation_OfAConditionalOfConcatenationAndAConcatenation_Joins() =>
        AssertAgreesWith("cat-cond-cat",
            Program((s1, s2, b1) => Cat(new Conditional(b1, Cat(s1, s2), s1), Cat(s1, s2))),
            "string:abab");

    [Test]
    public Task DeeplyNestedRepeatedConcatenation_IsStillCorrect() {
        static Node Nest(int depth, Variable s1, Variable s2) =>
            depth == 0 ? Cat(s1, s2) : Cat(Nest(depth - 1, s1, s2), Nest(depth - 1, s1, s2));
        // 2^6 copies of "ab": the repeat is at every level, not only the top.
        return AssertAgreesWith("deep-cat",
            Program((s1, s2, _) => Nest(6, s1, s2)),
            "string:" + string.Concat(Enumerable.Repeat("ab", 64)));
    }
}