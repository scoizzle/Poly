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
}