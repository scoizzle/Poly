using Poly.Interpretation;

namespace Poly.Tests.Interpretation;

/// <summary>
/// #374: a program that repeats a compound subtree (NotEqual(Add(i,1), Add(i,1))) used to stack-overflow the
/// VM compiler and kill the process. These programs must compile, run, and agree with the printed C#.
/// </summary>
public class RepeatedCompoundSubtreeTests {
    static Node Program(Func<Variable, Variable, Node> body) {
        var i = new Variable("i");
        var s = new Variable("s");
        return new Block([new Assignment(i, new Constant(5)), new Assignment(s, new Constant("a")), body(i, s)], [i, s]);
    }

    [Test]
    public async Task RepeatedIntAdd_InNotEqual_VmAgreesWithPrintedCSharp() {
        var row = AstParity.Compare("repeat#int", Program((i, _) => new NotEqual(new Add(i, new Constant(1)), new Add(i, new Constant(1)))));
        await Assert.That(row.Agrees).IsTrue().Because(row.Describe());
        await Assert.That(row.VmOutcome).IsEqualTo("bool:false");
    }

    [Test]
    public async Task RepeatedStringConcat_InNotEqual_VmAgreesWithPrintedCSharp() {
        var row = AstParity.Compare("repeat#str", Program((_, s) => new NotEqual(new Add(s, new Constant("c")), new Add(s, new Constant("c")))));
        await Assert.That(row.Agrees).IsTrue().Because(row.Describe());
    }

    [Test]
    public async Task RepeatedIntAdd_InEqual_VmAgreesWithPrintedCSharp() {
        var row = AstParity.Compare("repeat#int-eq", Program((i, _) => new Equal(new Add(i, new Constant(1)), new Add(i, new Constant(1)))));
        await Assert.That(row.Agrees).IsTrue().Because(row.Describe());
        await Assert.That(row.VmOutcome).IsEqualTo("bool:true");
    }
}