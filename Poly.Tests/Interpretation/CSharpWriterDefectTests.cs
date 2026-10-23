using System.Reflection;

using Poly.Interpretation;
using Poly.Interpretation.CSharp;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.Interpretation;

/// <summary>
/// The eleven writer defect classes the writer→Roslyn differential found (#279), one unit test per
/// class, where a unit assertion says more than a corpus row. Each failed before its fix; the corpus
/// rows in <see cref="AstParity.KnownDivergences"/> were the integration proof.
/// </summary>
public class CSharpWriterDefectTests {
    static int _assembly;

    /// <summary>A class with no faithful C# literal: the type the writer must name when it refuses.</summary>
    public sealed class LiteralOutlier {
        public override string ToString() => "no literal";
    }

    /// <summary>A nested CLR type: reflection spells the nesting with '+', C# with '.'.</summary>
    public sealed class Nested {
        public sealed class Inner {
            public override string ToString() => "inner";
        }
    }

    // ── 1. double literal formatting ─────────────────────────────────────────

    public static IEnumerable<double> Reals() => [
        7.0, 2.5, -0.0, 0.1, 1e300, double.Epsilon, double.MaxValue, double.MinValue,
        double.NaN, double.PositiveInfinity, double.NegativeInfinity,
    ];

    /// <summary>The constant prints a literal that re-reads as the same value, bit for bit.</summary>
    [Test]
    [MethodDataSource(nameof(Reals))]
    public async Task DoubleConstant_ReReadsAsTheSameValue(double value) {
        var printed = CSharpLanguage.Write(new Return(new Constant(value)));
        var executed = Evaluate(new Constant(value));

        await Assert.That(executed).IsTypeOf<double>().Because(printed);
        var reRead = (double)executed!;
        if (double.IsNaN(value)) {
            await Assert.That(double.IsNaN(reRead)).IsTrue().Because(printed);
            return;
        }
        await Assert.That(BitConverter.DoubleToInt64Bits(reRead))
            .IsEqualTo(BitConverter.DoubleToInt64Bits(value)).Because(printed);
    }

    [Test]
    public async Task DoubleConstant_KeepsItsDecimalPointAndType() {
        await Assert.That(CSharpLanguage.Write(new Return(new Constant(7.0)))).IsEqualTo("return 7.0;");
        await Assert.That(CSharpLanguage.Write(new Return(new Constant(-0.0)))).IsEqualTo("return -0.0;");
        await Assert.That(CSharpLanguage.Write(new Return(new Constant(double.NaN)))).IsEqualTo("return double.NaN;");
        await Assert.That(CSharpLanguage.Write(new Return(new Constant(double.PositiveInfinity))))
            .IsEqualTo("return double.PositiveInfinity;");
        await Assert.That(CSharpLanguage.Write(new Return(new Constant(double.NegativeInfinity))))
            .IsEqualTo("return double.NegativeInfinity;");
        await Assert.That(CSharpLanguage.Write(new Return(new Constant(2.5f)))).IsEqualTo("return 2.5f;");
        await Assert.That(CSharpLanguage.Write(new Return(new Constant(float.NaN)))).IsEqualTo("return float.NaN;");
    }

    // ── 2. unary minus parenthesisation ──────────────────────────────────────

    [Test]
    public async Task UnaryMinus_CompoundOperand_KeepsItsParentheses() {
        var printed = CSharpLanguage.Write(new UnaryMinus(new Add(new Variable("a"), new Variable("b"))));
        await Assert.That(printed).Contains("-(a + b)");
    }

    [Test]
    public async Task UnaryMinus_NegativeConstant_IsNotADecrement() {
        var printed = CSharpLanguage.Write(new Return(new UnaryMinus(new Constant(-99))));
        await Assert.That(printed).DoesNotContain("--");
        await Assert.That(Convert.ToInt64(Evaluate(new UnaryMinus(new Constant(-99))))).IsEqualTo(99L);
    }

    [Test]
    public async Task BitwiseNot_CompoundOperand_KeepsItsParentheses() {
        var printed = CSharpLanguage.Write(new BitwiseNot(new Add(new Variable("a"), new Variable("b"))));
        await Assert.That(printed).Contains("~(a + b)");
    }

    // ── 3. constant fallback ─────────────────────────────────────────────────

    [Test]
    public async Task ConstantWithoutLiteral_RejectsNamingTheType() {
        var error = Assert.Throws<InvalidOperationException>(
            () => CSharpLanguage.Write(new Constant(new LiteralOutlier())));
        await Assert.That(error.Message).Contains(nameof(LiteralOutlier));
    }

    [Test]
    public async Task ArrayConstant_PrintsAFaithfulArrayLiteral() {
        await Assert.That(CSharpLanguage.Write(new Return(new Constant(new[] { 1, 2, 3 }))))
            .IsEqualTo("return new int[] { 1, 2, 3 };");
        var executed = Evaluate(new Constant(new[] { 1, 2, 3 }));
        await Assert.That(executed).IsTypeOf<int[]>();
        await Assert.That((int[])executed!).IsEquivalentTo(new[] { 1, 2, 3 });
    }

    [Test]
    public async Task CollectionConstant_PrintsACollectionInitializer() {
        await Assert.That(CSharpLanguage.Write(new Return(new Constant(new List<int> { 1, 2, 3 }))))
            .IsEqualTo("return new List<int> { 1, 2, 3 };");
        var executed = Evaluate(new Constant(new List<int> { 1, 2, 3 }));
        await Assert.That(executed).IsTypeOf<List<int>>();
        await Assert.That((List<int>)executed!).IsEquivalentTo(new[] { 1, 2, 3 });
    }

    [Test]
    public async Task DateTimeConstant_PrintsAConstructorLiteral() {
        var value = new DateTime(2024, 1, 15, 10, 30, 0);
        var printed = CSharpLanguage.Write(new Return(new Constant(value)));
        await Assert.That(printed).StartsWith("return new System.DateTime(");
        await Assert.That(Evaluate(new Constant(value))).IsEqualTo(value);
    }

    // ── 4. block shadowing ───────────────────────────────────────────────────

    [Test]
    public async Task BlockShadowing_RenamesTheInnerLocal_AndExecutes() {
        var x = new Variable("x");
        var program = new Block(
            [new Assignment(x, new Constant(100)), new Block([new Assignment(x, new Constant(50)), x], [x])],
            [x]);

        await Assert.That(AstParity.Print(program)).Contains("var x_2 = 50");
        var row = await AstParity.AssertAgrees("block shadowing", program);
        await Assert.That(row.VmOutcome).IsEqualTo("long:50");
    }

    // ── 5. block value ───────────────────────────────────────────────────────

    [Test]
    public async Task BlockValue_ReachesItsConsumer() {
        var first = new Variable("x");
        var second = new Variable("x");
        var program = new Block(
            new Block([new Assignment(first, new Constant(10)), first], [first]),
            new Block([new Assignment(second, new Constant(20)), second], [second]));

        var row = await AstParity.AssertAgrees("block value", program);
        await Assert.That(row.VmOutcome).IsEqualTo("long:20");
    }

    [Test]
    public async Task TrailingValueInAStatementBlock_PrintsAsADiscard_NotABareExpression() {
        var printed = CSharpLanguage.Write(new Block(new Constant(10), new Variable("x")));
        await Assert.That(printed).Contains("_ = x;");
    }

    // ── 6. coalesce on a non-nullable operand ────────────────────────────────

    [Test]
    public async Task Coalesce_OnANonNullableOperand_PrintsThroughItsNullableForm() {
        await Assert.That(CSharpLanguage.Write(new Return(new Coalesce(new Constant(100), new Constant(42)))))
            .Contains("(int?)100 ?? 42");
        await Assert.That(Evaluate(new Coalesce(new Constant(100), new Constant(42)))).IsEqualTo(100);
        await Assert.That(Evaluate(new Coalesce(new Constant(null), new Constant(42)))).IsEqualTo(42);
    }

    // ── 7. lambda callee ─────────────────────────────────────────────────────

    [Test]
    public async Task LambdaCallee_IsParenthesised() {
        var v = new Parameter("v", TypeReference.To<int>());
        var program = new Invoke(new Lambda([v], new Add(v, new Constant(10))), new Constant(5));

        await Assert.That(AstParity.Print(program)).Contains(")(5)");
        await Assert.That(Convert.ToInt64(Evaluate(program))).IsEqualTo(15L);
    }

    // ── 8. nested CLR type names ─────────────────────────────────────────────

    [Test]
    public async Task NestedClrType_PrintsWithDots() {
        var printed = CSharpLanguage.Write(new Return(new New(TypeReference.To<Nested.Inner>())));
        await Assert.That(printed).Contains("Nested.Inner()");
        await Assert.That(printed).DoesNotContain("+");
    }

    // ── 9. as on incompatible types ──────────────────────────────────────────

    [Test]
    public async Task TypeAs_OnAValueTypedOperand_BoxesBeforeTheTryCast() {
        var program = new TypeAs(new Constant(42), TypeReference.To<string>());
        await Assert.That(AstParity.Print(program)).Contains("(object)42 as System.String");
        await Assert.That(Evaluate(program)).IsNull();
    }

    // ── 10. checked arithmetic and constant division by zero ─────────────────

    [Test]
    public async Task ConstantIntegralArithmetic_PrintsInAnUncheckedContext() {
        var program = new Add(new Constant(long.MaxValue), new Constant(1L));
        await Assert.That(AstParity.Print(program)).Contains("unchecked(");
        await Assert.That(Convert.ToInt64(Evaluate(program))).IsEqualTo(long.MinValue);
    }

    [Test]
    public async Task ConstantDivisionByZero_ThrowsAtRuntime() {
        var program = new Divide(new Constant(2), new Constant(0));
        await Assert.That(AstParity.Print(program)).DoesNotContain("/ 0");
        var row = await AstParity.AssertAgrees("constant division by zero", program);
        await Assert.That(row.VmOutcome).StartsWith("throw DivideByZeroException:");
    }

    // ── 11. lambda root ──────────────────────────────────────────────────────

    [Test]
    public async Task RootLambda_IsAProgramWithItsParameters() {
        var row = await AstParity.AssertAgrees("root lambda", new Lambda([], new Constant(42)));
        await Assert.That(row.VmOutcome).IsEqualTo("long:42");
    }

    /// <summary>Compiles the printed program with Roslyn and runs its entry point.</summary>
    static object? Evaluate(Node program) {
        var source = AstParity.Print(program);
        var assembly = ExportedCSharp.CompileAndLoad(source, "WriterDefect" + Interlocked.Increment(ref _assembly));
        var entry = assembly.GetType(AstParity.HarnessTypeName)!
            .GetMethod(AstParity.EntryPointName, BindingFlags.Public | BindingFlags.Static)!;
        return entry.Invoke(null, null);
    }
}