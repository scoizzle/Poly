using Poly.Interpretation;

namespace Poly.Tests.Interpretation;

/// <summary>
/// #374 integer width/overflow regressions: unchecked C# wraps at the operand width; the VM must too.
/// Each case runs the Roslyn differential and also pins a hand-written C# expected value.
/// </summary>
public class IntegerWidthParityTests {
    static Block Program(IReadOnlyList<(Variable Var, object Value)> locals, Node body) {
        List<Node> statements = [.. locals.Select(l => (Node)new Assignment(l.Var, new Constant(l.Value))), body];
        return new Block(statements, [.. locals.Select(l => l.Var)]);
    }

    [Test]
    public async Task Seed1_896_GeOfDivAndNegMinValue_AgreesAndIsTrue() {
        var i1 = new Variable("i1");
        var i2 = new Variable("i2");
        var body = new GreaterThanOrEqual(new Divide(i2, new Constant(2)), new UnaryMinus(i1));
        var row = AstParity.Compare("seed1-896", Program([(i1, int.MinValue), (i2, -1)], body));
        await Assert.That(row.Agrees).IsTrue().Because(row.Describe());
    }

    [Test]
    public async Task Seed1_896_VmOutcome_IsTrueByCSharpSemantics() {
        // 0 >= -int.MinValue; unchecked -int.MinValue wraps to int.MinValue.
        var i1 = new Variable("i1");
        var i2 = new Variable("i2");
        var body = new GreaterThanOrEqual(new Divide(i2, new Constant(2)), new UnaryMinus(i1));
        var row = AstParity.Compare("seed1-896-oracle", Program([(i1, int.MinValue), (i2, -1)], body));
        await Assert.That(row.VmOutcome).IsEqualTo("bool:true");
    }

    [Test]
    public async Task BoundaryArithmetic_WrapsAtOperandWidth_AndAgreesWithCSharp() {
        var a = new Variable("a");
        var b = new Variable("b");
        var cases = new List<(string Label, Block Program, string Expected)> {
            ("neg-int-min", Program([(a, int.MinValue)], new UnaryMinus(a)), "long:-2147483648"),
            ("int-max+1", Program([(a, int.MaxValue), (b, 1)], new Add(a, b)), "long:-2147483648"),
            ("int-min-1", Program([(a, int.MinValue), (b, 1)], new Subtract(a, b)), "long:2147483647"),
            ("int-max*2", Program([(a, int.MaxValue), (b, 2)], new Multiply(a, b)), "long:-2"),
            ("neg-long-min", Program([(a, long.MinValue)], new UnaryMinus(a)), "long:-9223372036854775808"),
            ("long-max+1", Program([(a, long.MaxValue), (b, 1L)], new Add(a, b)), "long:-9223372036854775808"),
            ("long-min-1", Program([(a, long.MinValue), (b, 1L)], new Subtract(a, b)), "long:9223372036854775807"),
            ("long-max*2", Program([(a, long.MaxValue), (b, 2L)], new Multiply(a, b)), "long:-2"),
            ("neg-min-eq-self", Program([(a, int.MinValue)], new Equal(a, new UnaryMinus(a))), "bool:true"),
            ("neg-min-ge-zero", Program([(a, int.MinValue), (b, 0)], new GreaterThanOrEqual(b, new UnaryMinus(a))), "bool:true"),
        };
        foreach (var (label, program, expected) in cases) {
            var row = AstParity.Compare(label, program);
            await Assert.That(row.Agrees).IsTrue().Because(row.Describe());
            await Assert.That(row.VmOutcome).IsEqualTo(expected).Because(label);
        }
    }

    [Test]
    public async Task Division_OfMinValueByMinusOne_ThrowsOverflowOnBothSides() {
        var a = new Variable("a");
        var b = new Variable("b");
        foreach (var (label, program) in new[] {
            ("int-min/-1", Program([(a, int.MinValue), (b, -1)], new Divide(a, b))),
            ("long-min/-1", Program([(a, long.MinValue), (b, -1L)], new Divide(a, b))),
        }) {
            var row = AstParity.Compare(label, program);
            await Assert.That(row.Agrees).IsTrue().Because(row.Describe());
        }
    }

    [Test]
    public async Task ConstantFolded_NegOfIntMinValueConstant_WrapsAtIntWidth() {
        // The operand is a Constant node, so ConstantFoldingPass folds -(int.MinValue) at analysis time
        // (the emitter never sees a UnaryMinus). Roslyn rejects a literal -int.MinValue (CS0220), so the
        // oracle is a hand-written C# value computed from a non-constant operand.
        int operand = int.MinValue;
        int expected = unchecked(-operand);
        var row = AstParity.Compare("fold-neg-int-min", new Block([new UnaryMinus(new Constant(int.MinValue))], []));
        await Assert.That(row.VmOutcome).IsEqualTo($"long:{expected}");
    }

    [Test]
    public async Task ConstantFolded_NegOfIntMinValueConstant_FoldsToIntConstant() {
        var node = new UnaryMinus(new Constant(int.MinValue));
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        int operand = int.MinValue;
        await Assert.That(exec.GetValue<long>()).IsEqualTo((long)unchecked(-operand));
    }

    public static class NegProbe {
        public static int ApplyInt(Func<int, int> f, int value) => f(value);
    }

    [Test]
    public async Task PureLambda_NegOfIntMinValueParameter_WrapsAtIntWidth() {
        var x = new Parameter("x", TypeReference.To<int>());
        var lambda = new Lambda([x], new UnaryMinus(x));
        using var exec = Interpreter.Execute(Interpreter.Compile(new Invoke(
            new Member(new ClrTypeReference(typeof(NegProbe)), nameof(NegProbe.ApplyInt)),
            lambda, new Constant(int.MinValue))));
        int operand = int.MinValue;
        await Assert.That(exec.GetValue<int>()).IsEqualTo(unchecked(-operand));
    }

    [Test]
    public async Task ConstantFolded_ParentOfNegIntMinValue_SeesWrappedIntNotPromotedLong() {
        // Every operand is a Constant, so the whole tree folds at analysis time and the parent reads the
        // folded UnaryMinus value. C#: with a = int.MinValue, (-a == a) and (0 >= -a) are both true.
        int a = int.MinValue;
        await Assert.That(unchecked(-a) == a).IsTrue();
        await Assert.That(0 >= unchecked(-a)).IsTrue();
        var cases = new (string Label, Node Body)[] {
            ("fold-neg-min-eq-min", new Equal(new UnaryMinus(new Constant(int.MinValue)), new Constant(int.MinValue))),
            ("fold-zero-ge-neg-min", new GreaterThanOrEqual(new Constant(0), new UnaryMinus(new Constant(int.MinValue)))),
        };
        foreach (var (label, body) in cases) {
            var row = AstParity.Compare(label, new Block([body], []));
            await Assert.That(row.VmOutcome).IsEqualTo("bool:true").Because(row.Describe());
        }
    }
}