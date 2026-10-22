using Poly.Interpretation;

namespace Poly.Tests.Interpretation;

/// <summary>
/// Mixed integer/IEEE comparisons promote the integer operand numerically.
/// Bitcasting the integer slot decodes its bits as a double, so <c>2 == 2.0</c>
/// becomes false and a loop guarded by <c>i &lt; 3.5</c> never terminates.
/// </summary>
public class MixedNumericComparisonTests {
    private static long Run(Node node) {
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        return exec.RawValue;
    }

    /// <summary>Assigns both operands to variables so constant folding cannot
    /// answer the comparison at analysis time.</summary>
    private static long Compare(Node left, Node right, Func<Node, Node, Node> compare) {
        var l = new Variable("l");
        var r = new Variable("r");
        Node node = new Block([
            new Assignment(l, left),
            new Assignment(r, right),
            compare(l, r)
        ], [l, r]);
        return Run(node);
    }

    [Test]
    public async Task Equal_LongAndDouble_ComparesNumerically() {
        long result = Compare(new Constant(2L), new Constant(2.0), (l, r) => new Equal(l, r));
        await Assert.That(result).IsEqualTo(1L);
    }

    [Test]
    public async Task NotEqual_LongAndDouble_ComparesNumerically() {
        long result = Compare(new Constant(2L), new Constant(2.0), (l, r) => new NotEqual(l, r));
        await Assert.That(result).IsEqualTo(0L);
    }

    [Test]
    public async Task LessThan_LongAndDouble_ComparesNumerically() {
        long result = Compare(new Constant(2L), new Constant(2.0), (l, r) => new LessThan(l, r));
        await Assert.That(result).IsEqualTo(0L);
    }

    [Test]
    public async Task LessThanOrEqual_LongAndDouble_ComparesNumerically() {
        long result = Compare(new Constant(9L), new Constant(2.5), (l, r) => new LessThanOrEqual(l, r));
        await Assert.That(result).IsEqualTo(0L);
    }

    [Test]
    public async Task GreaterThan_LongAndDouble_ComparesNumerically() {
        long result = Compare(new Constant(9L), new Constant(2.5), (l, r) => new GreaterThan(l, r));
        await Assert.That(result).IsEqualTo(1L);
    }

    [Test]
    public async Task GreaterThanOrEqual_LongAndDouble_ComparesNumerically() {
        long result = Compare(new Constant(2L), new Constant(2.0), (l, r) => new GreaterThanOrEqual(l, r));
        await Assert.That(result).IsEqualTo(1L);
    }

    [Test]
    public async Task LessThan_DoubleAndLong_ComparesNumerically() {
        long result = Compare(new Constant(2.5), new Constant(9L), (l, r) => new LessThan(l, r));
        await Assert.That(result).IsEqualTo(1L);
    }

    [Test]
    public async Task GreaterThan_LongAndFloat_ComparesNumerically() {
        long result = Compare(new Constant(3L), new Constant(2.5f), (l, r) => new GreaterThan(l, r));
        await Assert.That(result).IsEqualTo(1L);
    }

    [Test]
    public async Task Equal_LongWithInfinityBitPattern_ComparesNumerically() {
        long result = Compare(
            new Constant(0x7FF0000000000000L), new Constant(double.PositiveInfinity),
            (l, r) => new Equal(l, r));
        await Assert.That(result).IsEqualTo(0L);
    }

    [Test]
    public async Task LessThan_LongAndNaN_IsFalse() {
        long result = Compare(new Constant(9L), new Constant(double.NaN), (l, r) => new LessThan(l, r));
        await Assert.That(result).IsEqualTo(0L);
    }

    [Test]
    public async Task WhileLoop_LongComparedToDouble_Terminates() {
        var i = new Variable("i");
        var count = new Variable("count");
        Node node = new Block([
            new Assignment(i, new Constant(0L)),
            new Assignment(count, new Constant(0L)),
            new WhileLoop(
                new LessThan(i, new Constant(3.5)),
                new Block([
                    new Assignment(count, new Add(count, new Constant(1L))),
                    new Assignment(i, new Add(i, new Constant(1L)))
                ])),
            count
        ], [i, count]);
        // Normal mode enforces MaxLoopIterations, so a bad guard fails the test
        // instead of hanging the suite.
        using var exec = Interpreter.Execute(
            Interpreter.Compile(node), s => s.MaxLoopIterations = 10_000);
        await Assert.That(exec.GetValue<long>()).IsEqualTo(4L);
    }
}