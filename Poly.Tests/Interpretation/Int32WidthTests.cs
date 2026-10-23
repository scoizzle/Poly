using System.Linq.Expressions;

using Poly.Ast.Nodes;
using Poly.Interpretation;
using Poly.Interpretation.Vm;
using Poly.Interpretation.Vm.Abi;

namespace Poly.Tests.Interpretation;

/// <summary>
/// The integer-width rule: the arithmetic width comes from analysis, and the machine
/// performs the operation at that width. <c>int</c> is a representation alongside the
/// word, so <c>Expression.Add(int, int)</c> wraps at 32 bits because the CLR does — there
/// is no convention for the emitter and the constant folder to keep in sync.
/// <para>[#289](https://github.com/scoizzle/Poly/issues/289): <c>int.MaxValue + 1</c> folded
/// to <c>-2147483648</c> and, through variables, evaluated to <c>2147483648</c>. The two
/// halves of that reproduction are the first test here.</para>
/// </summary>
public class Int32WidthTests {

    /// <summary>#289's reproduction, both halves. The folded constant expression and the
    /// same sum through variables must answer the same <c>RawValue</c>, and that value is
    /// C#'s <c>unchecked</c> <c>int</c> answer.</summary>
    [Test, Timeout(10_000)]
    public async Task Int32MaxPlusOne_FoldedAndUnfolded_Agree(CancellationToken ct) {
        var folded = new Add(new Constant(int.MaxValue), new Constant(1));

        var a = new Variable("a");
        var b = new Variable("b");
        var unfolded = new Block([
            new Assignment(a, new Constant(int.MaxValue)),
            new Assignment(b, new Constant(1)),
            new Add(a, b),
        ], [a, b]);

        long foldedRaw = Raw(folded);
        long unfoldedRaw = Raw(unfolded);

        await Assert.That(foldedRaw).IsEqualTo(-2147483648L)
            .Because("the folder folds at the resolved width: int.MaxValue + 1 wraps at 32 bits");
        await Assert.That(unfoldedRaw).IsEqualTo(foldedRaw)
            .Because($"the unfolded sum must agree with the folded one; folded={foldedRaw}, "
                + $"unfolded={unfoldedRaw}");
    }

    /// <summary>A <c>long</c> declaration is 64-bit arithmetic and must not inherit the
    /// 32-bit wrap: <c>int.MaxValue</c> widened to <c>long</c> plus one is 2147483648.</summary>
    [Test, Timeout(10_000)]
    public async Task Int64Addition_DoesNotWrap(CancellationToken ct) {
        var a = new Variable("a");
        var b = new Variable("b");
        var program = new Block([
            new Assignment(a, new Constant(2147483647L)),
            new Assignment(b, new Constant(1L)),
            new Add(a, b),
        ], [a, b]);

        await Assert.That(Raw(program)).IsEqualTo(2147483648L)
            .Because("both operands resolve to Int64, so the addition runs at 64 bits");
        await Assert.That(Raw(new Add(new Constant(2147483647L), new Constant(1L))))
            .IsEqualTo(2147483648L)
            .Because("the folded form must agree with the unfolded one at 64 bits");
    }

    /// <summary>C# promotes a smaller integral type to <c>int</c> before an operator runs,
    /// so <c>short + short</c> is an <c>int</c> addition: it must not wrap at 16 bits, and
    /// the operation it emits is a 32-bit one.</summary>
    [Test, Timeout(10_000)]
    public async Task SmallIntegerArithmetic_PromotesToInt32(CancellationToken ct) {
        var a = new Variable("a");
        var b = new Variable("b");
        var program = new Block([
            new Assignment(a, new Constant((short)30000)),
            new Assignment(b, new Constant((short)30000)),
            new Add(a, b),
        ], [a, b]);

        await Assert.That(Raw(program)).IsEqualTo(60000L)
            .Because("short + short is an int addition, so 30000 + 30000 is 60000 and not -5536");

        var promoted = ArithmeticNodes(program, ExpressionType.Add)
            .Where(node => node.Type == typeof(int)
                && IsWordToInt(node.Left) && IsWordToInt(node.Right))
            .ToList();
        await Assert.That(promoted.Count).IsEqualTo(1)
            .Because("the program's own addition is the one 32-bit operation whose operands "
                + "widen from the word to int; found " + promoted.Count);
    }

    /// <summary>A word read widened to <c>int</c>: the shape a promoted smaller integral
    /// type takes, and not the shape of the emitter's own frame-pointer arithmetic.</summary>
    private static bool IsWordToInt(Expression expression) =>
        expression is UnaryExpression { NodeType: ExpressionType.Convert } convert
        && convert.Type == typeof(int)
        && convert.Operand.Type == typeof(long);

    /// <summary>An <c>int</c> read into a <c>long</c> expression widens exactly once, at the
    /// operand, and keeps its value.</summary>
    [Test, Timeout(10_000)]
    public async Task Int32ToInt64_WidensOnce(CancellationToken ct) {
        var a = new Variable("a");
        var b = new Variable("b");
        var program = new Block([
            new Assignment(a, new Constant(int.MaxValue)),
            new Assignment(b, new Constant(1L)),
            new Add(a, b),
        ], [a, b]);

        await Assert.That(Raw(program)).IsEqualTo(2147483648L)
            .Because("one operand is Int64, so the addition is 64-bit and the int widens");

        var addition = ArithmeticNodes(program, ExpressionType.Add)
            .Single(node => node.Type == typeof(long));
        var widening = Conversions(addition.Left).Concat(Conversions(addition.Right))
            .Where(convert => convert.Operand.Type == typeof(int) && convert.Type == typeof(long))
            .ToList();
        await Assert.That(widening.Count).IsEqualTo(1)
            .Because("the int operand converts to long once, at the operation — not per access "
                + "and not twice");
    }

    /// <summary>An <c>int</c> result assigned to a <c>long</c> slot widens without narrowing:
    /// the 32-bit answer survives the wider storage.</summary>
    [Test, Timeout(10_000)]
    public async Task Int32Result_IntoLongSlot_Widens(CancellationToken ct) {
        var a = new Variable("a");
        var b = new Variable("b");
        var acc = new Variable("acc");
        var program = new Block([
            new Assignment(a, new Constant(int.MaxValue)),
            new Assignment(b, new Constant(1)),
            new Assignment(acc, new Constant(0L)),
            new Assignment(acc, new Add(a, b)),
            acc,
        ], [a, b, acc]);

        await Assert.That(Raw(program)).IsEqualTo(-2147483648L)
            .Because("the addition is int-width, so it wraps before the long slot takes it; "
                + "storing it must not turn -2147483648 into 2147483648");
    }

    /// <summary>A width analysis cannot name is not silently 64-bit — that is how #289
    /// arose. The rule refuses it, and no corpus program reaches integer arithmetic through
    /// an unresolved width.</summary>
    [Test, Timeout(30_000)]
    public async Task UnknownIntegerWidth_IsNotSilently64Bit(CancellationToken ct) {
        await Assert.That(() => RingCodec.IntegerArithmeticWidth(null, typeof(int)))
            .Throws<InvalidOperationException>()
            .Because("an unresolved operand type has no width, and the word is not a safe guess");

        var unresolved = new List<string>();
        foreach (var corpusCase in AstParityTests.Corpus()) {
            try {
                Interpreter.Compile(corpusCase.Program);
            }
            catch (InvalidOperationException rejection) {
                if (rejection.Message.Contains("cannot resolve the integer width", StringComparison.Ordinal))
                    unresolved.Add($"{corpusCase.Label}: {rejection.Message}");
            }
            catch {
                // A row that fails for its own recorded reason is not this test's subject.
            }
        }
        await Assert.That(unresolved).IsEmpty()
            .Because("no corpus program may reach integer arithmetic through an unresolved width: "
                + string.Join(" | ", unresolved));
    }

    private static long Raw(Node program) {
        using var execution = Interpreter.Execute(Interpreter.Compile(program));
        return execution.RawValue;
    }

    private static IReadOnlyList<BinaryExpression> ArithmeticNodes(Node program, ExpressionType kind) {
        var finder = new ArithmeticFinder(kind);
        finder.Visit(AbiEmitter.Build(program, Interpreter.Analyze(program), CompilationMode.NoDebug).Expression);
        return finder.Found;
    }

    private static IReadOnlyList<UnaryExpression> Conversions(Expression expression) {
        var finder = new ConversionFinder();
        finder.Visit(expression);
        return finder.Found;
    }

    private sealed class ArithmeticFinder(ExpressionType kind) : ExpressionVisitor {
        public List<BinaryExpression> Found { get; } = [];

        protected override Expression VisitBinary(BinaryExpression node) {
            if (node.NodeType == kind)
                Found.Add(node);
            return base.VisitBinary(node);
        }
    }

    private sealed class ConversionFinder : ExpressionVisitor {
        public List<UnaryExpression> Found { get; } = [];

        protected override Expression VisitUnary(UnaryExpression node) {
            if (node.NodeType == ExpressionType.Convert)
                Found.Add(node);
            return base.VisitUnary(node);
        }
    }
}