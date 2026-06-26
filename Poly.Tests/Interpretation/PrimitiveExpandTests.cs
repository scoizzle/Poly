using Poly.Interpretation.Vm;
using Poly.Introspection.CommonLanguageRuntime;

using Prim = Poly.Syntax.Primitives;

namespace Poly.Tests.Interpretation;

public class PrimitiveExpandTests {
    internal static long ExecExpand(Node node) {
        var ctx = new AnalysisContext(ClrTypeDefinitionRegistry.Shared);
        var primitives = node.ToPrimitives(ctx);
        var primsList = primitives.ToList();
        primsList.Add(new Prim.Return());
        var linked = PrimitiveLinker.Link(primsList);
        var loweringResult = PrimitiveAdapter.ToLoweringResult(linked);
        var program = ProgramCompiler.Compile(loweringResult, mode: CompilationMode.Normal);
        using var state = new VmState(program) { MaxLoopIterations = 10_000 };
        var result = Vm.Execute(state);
        if (!result.HasValue)
            throw new InvalidOperationException($"VM returned void, kind={result.Kind}, status={state.Status}");
        return (long)(result.Value ?? 0);
    }

    [Test, Timeout(10_000)] public async Task Expand_Constant_ReturnsValue(CancellationToken ct) => await Assert.That(ExecExpand(new Constant(42))).IsEqualTo(42);
    [Test, Timeout(10_000)] public async Task Expand_Add_ReturnsSum(CancellationToken ct) => await Assert.That(ExecExpand(new Add(new Constant(5), new Constant(3)))).IsEqualTo(8);
    [Test, Timeout(10_000)] public async Task Expand_NestedAdd_ReturnsCorrectResult(CancellationToken ct) => await Assert.That(ExecExpand(new Add(new Add(new Constant(1), new Constant(2)), new Constant(3)))).IsEqualTo(6);
    [Test, Timeout(10_000)] public async Task Expand_Sub_ReturnsDifference(CancellationToken ct) => await Assert.That(ExecExpand(new Subtract(new Constant(10), new Constant(3)))).IsEqualTo(7);
    [Test, Timeout(10_000)] public async Task Expand_Mul_ReturnsProduct(CancellationToken ct) => await Assert.That(ExecExpand(new Multiply(new Constant(7), new Constant(6)))).IsEqualTo(42);
    [Test, Timeout(10_000)] public async Task Expand_Div_ReturnsQuotient(CancellationToken ct) => await Assert.That(ExecExpand(new Divide(new Constant(10), new Constant(3)))).IsEqualTo(3);
    [Test, Timeout(10_000)] public async Task Expand_Mod_ReturnsRemainder(CancellationToken ct) => await Assert.That(ExecExpand(new Modulo(new Constant(10), new Constant(3)))).IsEqualTo(1);
    [Test, Timeout(10_000)] public async Task Expand_Eq_ReturnsOneWhenEqual(CancellationToken ct) => await Assert.That(ExecExpand(new Equal(new Constant(5), new Constant(5)))).IsEqualTo(1);
    [Test, Timeout(10_000)] public async Task Expand_Eq_ReturnsZeroWhenNotEqual(CancellationToken ct) => await Assert.That(ExecExpand(new Equal(new Constant(5), new Constant(3)))).IsEqualTo(0);
    [Test, Timeout(10_000)] public async Task Expand_Gt_ReturnsOneWhenGreater(CancellationToken ct) => await Assert.That(ExecExpand(new GreaterThan(new Constant(10), new Constant(3)))).IsEqualTo(1);
    [Test, Timeout(10_000)] public async Task Expand_Lt_ReturnsOneWhenLess(CancellationToken ct) => await Assert.That(ExecExpand(new LessThan(new Constant(3), new Constant(10)))).IsEqualTo(1);
    [Test, Timeout(10_000)] public async Task Expand_NullForgiving_Passthrough(CancellationToken ct) => await Assert.That(ExecExpand(new NullForgiving(new Constant(42)))).IsEqualTo(42);
    [Test, Timeout(10_000)] public async Task Expand_ThisReference_ReturnsZero(CancellationToken ct) => await Assert.That(ExecExpand(new ThisReference())).IsEqualTo(0);
    [Test, Timeout(10_000)] public async Task Expand_SuspendNode_Passthrough(CancellationToken ct) => await Assert.That(ExecExpand(new SuspendNode(new Constant(42)))).IsEqualTo(42);
    [Test, Timeout(10_000)] public async Task Expand_Default_ReturnsZero(CancellationToken ct) => await Assert.That(ExecExpand(new Default())).IsEqualTo(0);
    [Test, Timeout(10_000)] public async Task Expand_Return_WithValue_ReturnsValue(CancellationToken ct) => await Assert.That(ExecExpand(new Return(new Constant(42)))).IsEqualTo(42);
    [Test, Timeout(10_000)] public async Task Expand_PopCount_ReturnsBitCount(CancellationToken ct) => await Assert.That(ExecExpand(new PopCount(new Constant(11L)))).IsEqualTo(3);
    [Test, Timeout(10_000)] public async Task Expand_BitwiseAnd_ReturnsAnd(CancellationToken ct) => await Assert.That(ExecExpand(new BitwiseAnd(new Constant(6), new Constant(3)))).IsEqualTo(2);
    [Test, Timeout(10_000)] public async Task Expand_BitwiseOr_ReturnsOr(CancellationToken ct) => await Assert.That(ExecExpand(new BitwiseOr(new Constant(6), new Constant(3)))).IsEqualTo(7);

    [Test, Timeout(10_000)]
    public async Task Expand_Block_WithVariable_ReturnsValue(CancellationToken ct) {
        var x = new Variable("x");
        await Assert.That(ExecExpand(new Block([new Assignment(x, new Constant(42)), x], [x]))).IsEqualTo(42);
    }

    [Test, Timeout(10_000)]
    public async Task Expand_Block_MultipleStatements_ReturnsLast(CancellationToken ct) {
        var x = new Variable("x"); var y = new Variable("y");
        await Assert.That(ExecExpand(new Block([new Assignment(x, new Constant(10)), new Assignment(y, new Constant(20)), new Add(x, y)], [x, y]))).IsEqualTo(30);
    }

    [Test, Timeout(10_000)]
    public async Task Expand_Assignment_Chain_Works(CancellationToken ct) {
        var x = new Variable("x");
        await Assert.That(ExecExpand(new Block([new Assignment(x, new Assignment(x, new Constant(5)))], [x]))).IsEqualTo(5);
    }

    [Test, Timeout(10_000)] public async Task Expand_If_TrueBranch_ReturnsThen(CancellationToken ct) => await Assert.That(ExecExpand(new IfStatement(new Constant(1), new Constant(42), new Constant(0)))).IsEqualTo(42);
    [Test, Timeout(10_000)] public async Task Expand_If_FalseBranch_ReturnsElse(CancellationToken ct) => await Assert.That(ExecExpand(new IfStatement(new Constant(0), new Constant(1), new Constant(42)))).IsEqualTo(42);
    [Test, Timeout(10_000)] public async Task Expand_If_NoElse_ReturnsConditionValue(CancellationToken ct) => await Assert.That(ExecExpand(new IfStatement(new Constant(42), new Constant(99)))).IsEqualTo(99);
    [Test, Timeout(10_000)] public async Task Expand_Conditional_TrueBranch_ReturnsTrueValue(CancellationToken ct) => await Assert.That(ExecExpand(new Conditional(new Constant(1), new Constant(42), new Constant(0)))).IsEqualTo(42);
    [Test, Timeout(10_000)] public async Task Expand_Conditional_FalseBranch_ReturnsFalseValue(CancellationToken ct) => await Assert.That(ExecExpand(new Conditional(new Constant(0), new Constant(1), new Constant(99)))).IsEqualTo(99);
    [Test, Timeout(10_000)] public async Task Expand_Coalesce_NonNull_ReturnsLhs(CancellationToken ct) => await Assert.That(ExecExpand(new Coalesce(new Constant(42), new Constant(99)))).IsEqualTo(42);
    [Test, Timeout(10_000)] public async Task Expand_Coalesce_Null_ReturnsRhs(CancellationToken ct) => await Assert.That(ExecExpand(new Coalesce(new Constant(0), new Constant(99)))).IsEqualTo(99);

    [Test, Timeout(10_000)]
    public async Task Expand_WhileLoop_CountsToFive(CancellationToken ct) {
        var i = new Variable("i");
        await Assert.That(ExecExpand(new Block([new Assignment(i, new Constant(0)), new WhileLoop(new LessThan(i, new Constant(5)), new Assignment(i, new Add(i, new Constant(1)))), i], [i]))).IsEqualTo(5);
    }

    [Test, Timeout(10_000)]
    public async Task Expand_DoWhileLoop_CountsToFive(CancellationToken ct) {
        var i = new Variable("i");
        await Assert.That(ExecExpand(new Block([new Assignment(i, new Constant(0)), new DoWhileLoop(new Assignment(i, new Add(i, new Constant(1))), new LessThan(i, new Constant(5))), i], [i]))).IsEqualTo(5);
    }

    [Test, Timeout(10_000)]
    public async Task Expand_ForLoop_SumToTen(CancellationToken ct) {
        var sum = new Variable("sum"); var i = new Variable("i");
        await Assert.That(ExecExpand(new Block([new Assignment(sum, new Constant(0)), new ForLoop(new Assignment(i, new Constant(0)), new LessThan(i, new Constant(10)), new Assignment(i, new Add(i, new Constant(1))), new Assignment(sum, new Add(sum, i))), sum], [sum, i]))).IsEqualTo(45);
    }

    [Test, Timeout(10_000)] public async Task Expand_LabelDeclaration_ExecutesBody(CancellationToken ct) => await Assert.That(ExecExpand(new LabelDeclaration("start", new Constant(42)))).IsEqualTo(42);
    [Test, Timeout(10_000)] public async Task Expand_UsingStatement_ExecutesBody(CancellationToken ct) => await Assert.That(ExecExpand(new UsingStatement(new Constant(0), new Constant(42)))).IsEqualTo(42);
    [Test, Timeout(10_000)] public async Task Expand_ForEachLoop_ExecutesBody(CancellationToken ct) => await Assert.That(ExecExpand(new ForEachLoop(new Variable("x"), new Constant(0), new Constant(42)))).IsEqualTo(42);
    [Test, Timeout(10_000)] public async Task Expand_TryCatchFinally_ExecutesTryBlock(CancellationToken ct) => await Assert.That(ExecExpand(new TryCatchFinally(new Constant(42)))).IsEqualTo(42);

    [Test, Timeout(10_000)] public async Task Expand_Member_ReturnsZero(CancellationToken ct) => await Assert.That(ExecExpand(new Member(new Constant(42), "Dummy"))).IsEqualTo(0);
    [Test, Timeout(10_000)] public async Task Expand_TypeAs_Passthrough(CancellationToken ct) => await Assert.That(ExecExpand(new TypeAs(new Constant(42), TypeReference.To<int>()))).IsEqualTo(42);
    [Test, Timeout(10_000)] public async Task Expand_TypeCast_Passthrough(CancellationToken ct) => await Assert.That(ExecExpand(new TypeCast(new Constant(42), TypeReference.To<int>()))).IsEqualTo(42);
    [Test, Timeout(10_000)] public async Task Expand_TypeIs_Passthrough(CancellationToken ct) => await Assert.That(ExecExpand(new TypeIs(new Constant(42), TypeReference.To<int>()))).IsEqualTo(42);
    [Test, Timeout(10_000)] public async Task Expand_Await_Passthrough(CancellationToken ct) => await Assert.That(ExecExpand(new Await(new Constant(42)))).IsEqualTo(42);
    [Test, Timeout(10_000)] public async Task Expand_Lambda_ReturnsBodyValue(CancellationToken ct) => await Assert.That(ExecExpand(new Lambda([], new Constant(42)))).IsEqualTo(0);

    [Test, Timeout(10_000)]
    public async Task Expand_StridedSetBits_Expands(CancellationToken ct) {
        var result = new StridedSetBits(new Constant(0), new Constant(0), new Constant(0), new Constant(0));
        var ctx = new AnalysisContext(ClrTypeDefinitionRegistry.Shared);
        var prims = result.ToPrimitives(ctx).ToList();
        await Assert.That(prims.Count).IsEqualTo(5); // 4 operands + StridedSet
    }
}