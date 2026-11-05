using Poly.Interpretation;
using Poly.Interpretation.Analysis.ConstantFolding;
using Poly.Interpretation.Analysis.ControlFlow;
using Poly.Interpretation.Analysis.Semantics;

namespace Poly.Tests.Interpretation;

/// <summary>
/// The constant-folding pass rewrites <c>x / 2^n</c> to <c>x &gt;&gt; n</c> and <c>x % 2^n</c> to
/// <c>x &amp; (2^n - 1)</c>. Those identities hold for unsigned <c>x</c> only: C# division and
/// remainder truncate toward zero, while a shift and a mask work on the two's complement bits, so
/// every negative dividend gets a different answer. The pass must not rewrite a signed operand.
/// </summary>
public class ConstantFoldingStrengthReductionTests {
    static AnalysisResult Analyze(Node ast) => new AnalyzerBuilder()
        .UseThisReferenceContext()
        .UseVariableScopeValidator()
        .UseTypeAndMemberResolver()
        .UseSideEffectAnalysis()
        .UseJumpTargetResolution()
        .UseConstantFolding()
        .UseControlFlowAnalysis()
        .Build()
        .Analyze(ast);

    static long Run(Block program) {
        try {
            var result = Interpreter.Execute(Interpreter.Compile(program));
            return Convert.ToInt64(result.Result.Value, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (Exception ex) {
            throw new InvalidOperationException($"compile or execute refused the program: {ex.GetType().Name}: {ex.Message}", ex);
        }
    }

    /// <summary>A program that reads a value through a local, so no operand is a folded constant.</summary>
    static (Block Program, Node Operation) Program(object dividend, Node divisor, bool modulo) {
        var x = new Variable("x");
        var y = new Variable("y");
        Node operation = modulo ? new Modulo(x, divisor) : new Divide(x, divisor);
        return (new Block([new Assignment(x, Wrap(dividend)), new Assignment(y, operation), y], [x, y]), operation);
    }

    [Test]
    [Arguments(-7, 2, -3)]
    [Arguments(-9, 2, -4)]
    [Arguments(-9, 4, -2)]
    [Arguments(7, 2, 3)]
    public async Task Division_ByPowerOfTwo_TruncatesTowardZero(int dividend, int divisor, long expected) {
        var (program, operation) = Program(dividend, Wrap(divisor), modulo: false);
        var analysis = Analyze(program);

        await Assert.That(analysis.GetNodeReplacement(operation)).IsNull()
            .Because("a signed division must not be rewritten to a shift");
        await Assert.That(Run(program)).IsEqualTo(expected);
    }

    [Test]
    [Arguments(-7, 2, -1)]
    [Arguments(-9, 4, -1)]
    [Arguments(7, 2, 1)]
    public async Task Modulo_ByPowerOfTwo_KeepsTheDividendSign(int dividend, int divisor, long expected) {
        var (program, operation) = Program(dividend, Wrap(divisor), modulo: true);
        var analysis = Analyze(program);

        await Assert.That(analysis.GetNodeReplacement(operation)).IsNull()
            .Because("a signed remainder must not be rewritten to a mask");
        await Assert.That(Run(program)).IsEqualTo(expected);
    }

    [Test]
    public async Task Division_ByPowerOfTwo_UnsignedDividend_StillRewrites() {
        var (program, operation) = Program(9u, Wrap(2u), modulo: false);
        var analysis = Analyze(program);

        await Assert.That(analysis.GetNodeReplacement(operation)).IsAssignableTo<ShiftRight>()
            .Because("the shift identity is exact for an unsigned dividend, so the optimization stays");
    }

    [Test]
    public async Task Modulo_ByPowerOfTwo_UnsignedDividend_StillRewrites() {
        var (program, operation) = Program(9u, Wrap(4u), modulo: true);
        var analysis = Analyze(program);

        await Assert.That(analysis.GetNodeReplacement(operation)).IsAssignableTo<BitwiseAnd>()
            .Because("the mask identity is exact for an unsigned dividend, so the optimization stays");
    }

    [Test]
    public async Task Multiplication_ByPowerOfTwo_SignedDividend_StillRewrites() {
        var x = new Variable("x");
        var y = new Variable("y");
        var product = new Multiply(x, Wrap(4));
        var program = new Block([new Assignment(x, Wrap(-7)), new Assignment(y, product), y], [x, y]);
        var analysis = Analyze(program);

        await Assert.That(analysis.GetNodeReplacement(product)).IsAssignableTo<ShiftLeft>()
            .Because("multiplication by 2^n is a left shift for signed and unsigned alike");
    }
}