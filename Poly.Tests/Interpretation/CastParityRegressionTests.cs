using Poly.Interpretation;

namespace Poly.Tests.Interpretation;

/// <summary>
/// #374 regressions found by the seeded generator: a cast of a compound operand lost its brackets in the
/// printed C#, and a widening cast under a conditional branch returned the double's raw bits in the VM.
/// Each repro runs through the Roslyn differential, so a regression prints interpreter vs printed value.
/// </summary>
public class CastParityRegressionTests {
    static Block Program(IReadOnlyList<(Variable Var, object Value)> locals, Node body) {
        List<Node> statements = [.. locals.Select(l => (Node)new Assignment(l.Var, new Constant(l.Value))), body];
        return new Block(statements, [.. locals.Select(l => l.Var)]);
    }

    [Test]
    public async Task Cast_OfCompoundOperand_PrintsBracketsAndAgreesWithTheVm() {
        // seeds 374 #67, #88, #91: Cast<Long>(Add(Add(i2,i1),Mul(i1,i2))) printed '(System.Int64)i2 + i1 + i1 * i2'.
        var i1 = new Variable("i1");
        var i2 = new Variable("i2");
        var body = new TypeCast(new Add(new Add(i2, i1), new Multiply(i1, i2)), TypeReference.To<long>());

        var row = AstParity.Compare("cast-compound", Program([(i1, int.MaxValue), (i2, 2)], body));

        await Assert.That(row.Agrees).IsTrue().Because(row.Describe());
        await Assert.That(row.Source!).Contains("(System.Int64)(");
    }

    [Test]
    public async Task Cast_OfEveryLowerPrecedenceOperand_IsBracketed() {
        var i1 = new Variable("i1");
        var b1 = new Variable("b1");
        var operands = new Node[] {
            new Add(i1, i1), new Subtract(i1, i1), new Multiply(i1, i1), new Divide(i1, i1),
            new Conditional(b1, i1, i1),
        };
        foreach (var operand in operands) {
            var row = AstParity.Compare("cast-" + operand.GetType().Name,
                Program([(i1, 3), (b1, true)], new TypeCast(operand, TypeReference.To<long>())));
            await Assert.That(row.Agrees).IsTrue().Because(row.Describe());
            await Assert.That(row.Source!).Contains("(System.Int64)(");
        }
    }

    [Test]
    public async Task Conditional_WithWideningCastBranch_YieldsTheValueNotTheBits() {
        // seeds 374 #58, #97: Conditional(b, d, Cast<Double>(i)) returned the double's raw bit pattern.
        var b1 = new Variable("b1");
        var d1 = new Variable("d1");
        var i1 = new Variable("i1");
        var body = new Conditional(b1, d1, new TypeCast(i1, TypeReference.To<double>()));

        var row = AstParity.Compare("cond-cast",
            Program([(b1, false), (d1, 1.5), (i1, 3)], body));

        await Assert.That(row.Agrees).IsTrue().Because(row.Describe());
        await Assert.That(row.VmOutcome).IsEqualTo("double:3");
    }
}