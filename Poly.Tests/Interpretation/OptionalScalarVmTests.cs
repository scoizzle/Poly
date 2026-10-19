using Poly.Ast.Nodes;
using Poly.Interpretation;
using Poly.Introspection;

namespace Poly.Tests.Interpretation;

/// <summary>
/// Optional scalar values in the VM (`long?` and `bool?`). The slot is a heap handle:
/// 0 is absent, otherwise a cell holds the boxed value — so a present 0/false is
/// distinguishable from absent. Presence is `x == null` / `x != null`; an arithmetic or
/// relational use unboxes, and a relational use of an absent operand throws.
/// </summary>
public class OptionalScalarVmTests {
    private static ExecutionResult Execute(Node node, params object?[] args) =>
        Interpreter.Execute(Interpreter.Compile(node), s => s.SetArgs(args));

    private static long ValueOf(Node node, params object?[] args) {
        using var exec = Execute(node, args);
        return exec.GetValue<long>();
    }

    private static Parameter LongNullable() => new("x", TypeReference.To<long?>());
    private static Parameter BoolNullable() => new("b", TypeReference.To<bool?>());

    // ── presence ─────────────────────────────────────────────────────────────

    [Test]
    public async Task Presence_Long_AbsentAndPresentZeroAreDistinct() {
        var x = LongNullable();
        await Assert.That(ValueOf(new NotEqual(x, new Constant(null)), new object?[] { null })).IsEqualTo(0L);
        await Assert.That(ValueOf(new NotEqual(x, new Constant(null)), 0L)).IsEqualTo(1L);
        await Assert.That(ValueOf(new NotEqual(x, new Constant(null)), 7L)).IsEqualTo(1L);

        await Assert.That(ValueOf(new Equal(x, new Constant(null)), new object?[] { null })).IsEqualTo(1L);
        await Assert.That(ValueOf(new Equal(x, new Constant(null)), 0L)).IsEqualTo(0L);
    }

    [Test]
    public async Task Presence_Bool_AbsentAndPresentFalseAreDistinct() {
        var b = BoolNullable();
        await Assert.That(ValueOf(new NotEqual(b, new Constant(null)), new object?[] { null })).IsEqualTo(0L);
        await Assert.That(ValueOf(new NotEqual(b, new Constant(null)), false)).IsEqualTo(1L);
        await Assert.That(ValueOf(new NotEqual(b, new Constant(null)), true)).IsEqualTo(1L);
    }

    // ── arithmetic / comparison ──────────────────────────────────────────────

    [Test]
    public async Task Arithmetic_PresentOptional_Unboxes() {
        var x = LongNullable();
        await Assert.That(ValueOf(new Add(x, new Constant(1L)), 50L)).IsEqualTo(51L);
        await Assert.That(ValueOf(new Multiply(x, new Constant(3L)), 4L)).IsEqualTo(12L);
    }

    [Test]
    public async Task Relational_PresentOptional_Unboxes() {
        var x = LongNullable();
        await Assert.That(ValueOf(new LessThan(x, new Constant(5L)), 3L)).IsEqualTo(1L);
        await Assert.That(ValueOf(new GreaterThanOrEqual(x, new Constant(5L)), 5L)).IsEqualTo(1L);
    }

    [Test]
    public async Task Relational_AbsentOptional_Throws() {
        var x = LongNullable();
        await Assert.That(() => ValueOf(new LessThan(x, new Constant(5L)), new object?[] { null }))
            .Throws<InvalidOperationException>();
        await Assert.That(() => ValueOf(new GreaterThan(x, new Constant(5L)), new object?[] { null }))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Arithmetic_AbsentOptional_Throws() {
        var x = LongNullable();
        await Assert.That(() => ValueOf(new Add(x, new Constant(1L)), new object?[] { null }))
            .Throws<InvalidOperationException>();
    }

    // ── store ────────────────────────────────────────────────────────────────

    [Test]
    public async Task Store_PresentZero_IsNotAbsent() {
        var v = new Variable("v");
        var node = new Block([
            new Assignment(v, new TypeCast(new Constant(0L), TypeReference.To<long?>())),
            new NotEqual(v, new Constant(null))
        ], [v]);
        await Assert.That(ValueOf(node)).IsEqualTo(1L);
    }

    [Test]
    public async Task Store_PresentValue_KeepsTheValue() {
        var v = new Variable("v");
        var node = new Block([
            new Assignment(v, new TypeCast(new Constant(7L), TypeReference.To<long?>())),
            new Add(v, new Constant(1L))
        ], [v]);
        await Assert.That(ValueOf(node)).IsEqualTo(8L);
    }

    [Test]
    public async Task Store_Absent_IsTheNullHandle() {
        var v = new Variable("v");
        using var exec = Execute(new Block([new Assignment(v, new Constant(null)), v], [v]));
        await Assert.That(exec.RawValue).IsEqualTo(0L);
    }

    [Test]
    public async Task Store_PresentBool_IsNotAbsent() {
        var v = new Variable("v");
        var node = new Block([
            new Assignment(v, new TypeCast(new Constant(false), TypeReference.To<bool?>())),
            new NotEqual(v, new Constant(null))
        ], [v]);
        await Assert.That(ValueOf(node)).IsEqualTo(1L);
    }

    [Test]
    public async Task Condition_Relational_UnboxesAndThrowsOnAbsent() {
        var x = LongNullable();
        var cond = new Conditional(new LessThan(x, new Constant(5L)), new Constant(1L), new Constant(0L));
        await Assert.That(ValueOf(cond, 3L)).IsEqualTo(1L);
        await Assert.That(ValueOf(cond, 9L)).IsEqualTo(0L);
        await Assert.That(() => ValueOf(cond, new object?[] { null })).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ArithmeticResult_ComposesAsAScalar() {
        var x = LongNullable();
        // The sum of a present optional is a scalar slot, not a handle: it compares and stores
        // as its value.
        await Assert.That(ValueOf(new Equal(new Add(x, new Constant(1L)), new Constant(51L)), 50L)).IsEqualTo(1L);

        var v = new Variable("v");
        var zeroSumStored = new Block([
            new Assignment(v, new TypeCast(new Add(x, new Constant(-50L)), TypeReference.To<long?>())),
            new NotEqual(v, new Constant(null))
        ], [v]);
        await Assert.That(ValueOf(zeroSumStored, 50L)).IsEqualTo(1L);
    }

    [Test]
    public async Task Relational_AbsentOrNullOperand_Throws() {
        var x = LongNullable();
        await Assert.That(() => ValueOf(new LessThan(x, new Constant(5L)), new object?[] { null }))
            .Throws<InvalidOperationException>();
        await Assert.That(() => ValueOf(new LessThan(x, new Constant(null)), 3L))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Equality_Scalar_UnboxesAndTreatsAbsentAsNotNull() {
        var x = LongNullable();
        await Assert.That(ValueOf(new Equal(x, new Constant(0L)), 0L)).IsEqualTo(1L);
        await Assert.That(ValueOf(new Equal(x, new Constant(0L)), 7L)).IsEqualTo(0L);
        await Assert.That(ValueOf(new Equal(x, new Constant(7L)), 7L)).IsEqualTo(1L);
        // An absent optional is not equal to a scalar.
        await Assert.That(ValueOf(new Equal(x, new Constant(0L)), new object?[] { null })).IsEqualTo(0L);
        await Assert.That(ValueOf(new NotEqual(x, new Constant(0L)), new object?[] { null })).IsEqualTo(1L);
    }

    private sealed class OptionalHost {
        public long? N { get; set; }
        public bool? B { get; set; }
    }

    [Test]
    public async Task MemberWrite_PresentZeroAndFalse_AreNotAbsent() {
        var host = new OptionalHost();
        var n = new Member(new Constant(host), nameof(OptionalHost.N));
        var zeroStore = new Block([
            new Assignment(n, new TypeCast(new Constant(0L), TypeReference.To<long?>())),
            new NotEqual(n, new Constant(null))
        ]);
        await Assert.That(ValueOf(zeroStore)).IsEqualTo(1L);

        var valueStore = new Block([
            new Assignment(n, new TypeCast(new Constant(7L), TypeReference.To<long?>())),
            new Add(n, new Constant(1L))
        ]);
        await Assert.That(ValueOf(valueStore)).IsEqualTo(8L);

        var b = new Member(new Constant(host), nameof(OptionalHost.B));
        var boolStore = new Block([
            new Assignment(b, new TypeCast(new Constant(false), TypeReference.To<bool?>())),
            new NotEqual(b, new Constant(null))
        ]);
        await Assert.That(ValueOf(boolStore)).IsEqualTo(1L);
    }

    [Test]
    public async Task Store_PlainScalarIntoOptionalVariable_Boxes() {
        // The binding is typed `long?`; a later plain-scalar store must box so the read unboxes
        // the cell to its value (0), not the handle.
        var v = new Variable("v");
        var node = new Block([
            new Assignment(v, new TypeCast(new Constant(5L), TypeReference.To<long?>())),
            new Assignment(v, new Constant(0L)),
            new Add(v, new Constant(1L))
        ], [v]);
        await Assert.That(ValueOf(node)).IsEqualTo(1L);
    }

    [Test]
    public async Task BoolOptional_LogicalValues_Unbox() {
        var b = BoolNullable();
        await Assert.That(ValueOf(new And(b, new Constant(true)), false)).IsEqualTo(0L);
        await Assert.That(ValueOf(new Or(b, new Constant(false)), false)).IsEqualTo(0L);
        await Assert.That(ValueOf(new Or(b, new Constant(false)), true)).IsEqualTo(1L);
    }

    [Test]
    public async Task Store_PlainScalarIntoNullableMember_Boxes() {
        var host = new OptionalHost();
        var n = new Member(new Constant(host), nameof(OptionalHost.N));
        var zeroStore = new Block([new Assignment(n, new Constant(0L)), new NotEqual(n, new Constant(null))]);
        await Assert.That(ValueOf(zeroStore)).IsEqualTo(1L);

        var valueStore = new Block([new Assignment(n, new Constant(7L)), new Add(n, new Constant(1L))]);
        await Assert.That(ValueOf(valueStore)).IsEqualTo(8L);
    }

    [Test]
    public async Task BitwiseNot_PresentOptional_UnboxesAndThrowsOnAbsent() {
        var x = LongNullable();
        await Assert.That(ValueOf(new BitwiseNot(x), 50L)).IsEqualTo(~50L);
        await Assert.That(() => ValueOf(new BitwiseNot(x), new object?[] { null })).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task BoolOptional_AsCondition_Unboxes() {
        var b = BoolNullable();
        var cond = new Conditional(b, new Constant(1L), new Constant(0L));
        await Assert.That(ValueOf(cond, true)).IsEqualTo(1L);
        await Assert.That(ValueOf(cond, false)).IsEqualTo(0L);
    }

    private static class OptionalMarshalling {
        public static string LongText(long? v) => v?.ToString() ?? "null";
        public static string BoolText(bool? v) => v?.ToString() ?? "null";
    }

    private static Node CallStatic(string name, params Node[] args) =>
        new Invoke(new Member(new ClrTypeReference(typeof(OptionalMarshalling)), name), args);

    [Test]
    public async Task Marshalling_OptionalArgument_ToClrParameter() {
        var x = LongNullable();
        var b = BoolNullable();
        await Assert.That(ValueOfText(CallStatic(nameof(OptionalMarshalling.LongText), x), 0L)).IsEqualTo("0");
        await Assert.That(ValueOfText(CallStatic(nameof(OptionalMarshalling.LongText), x), new object?[] { null })).IsEqualTo("null");
        await Assert.That(ValueOfText(CallStatic(nameof(OptionalMarshalling.BoolText), b), false)).IsEqualTo("False");
        await Assert.That(ValueOfText(CallStatic(nameof(OptionalMarshalling.BoolText), b), new object?[] { null })).IsEqualTo("null");
    }

    private static string ValueOfText(Node node, params object?[] args) {
        using var exec = Execute(node, args);
        return exec.Result.Value?.ToString() ?? "";
    }

    [Test]
    public async Task Foreach_OptionalElement_IsAHandle() {
        // A nullable element list: null is absent, 1L is present — distinction preserved.
        var array = new Constant(new long?[] { 1L, null, 2L });
        var item = new Variable("x");
        var present = new Variable("n");
        var node = new Block([
            new Assignment(present, new Constant(0L)),
            new ForEachLoop(item, array,
                new Assignment(present, new Add(present,
                    new Conditional(new NotEqual(item, new Constant(null)), new Constant(1L), new Constant(0L)))),
                DeclaredType: TypeReference.To<long?>()),
            present
        ], [present]);
        await Assert.That(ValueOf(node)).IsEqualTo(2L);
    }

    [Test]
    public async Task Coalesce_TwoOptionals_KeepsTheHandle() {
        var a = new Parameter("a", TypeReference.To<long?>());
        var b = new Parameter("b", TypeReference.To<long?>());
        // Absent left selects the right's handle, so a present 0 stays present.
        await Assert.That(ValueOf(new NotEqual(new Coalesce(a, b), new Constant(null)),
            new object?[] { null, 0L })).IsEqualTo(1L);
        // Present left unboxes to its value.
        await Assert.That(ValueOf(new Add(new Coalesce(a, b), new Constant(1L)), 7L, 0L)).IsEqualTo(8L);
    }

    // ── bool cells are shared, not reallocated per write ─────────────────────

    [Test]
    public async Task BoolCells_AreSharedAcrossWrites() {
        static long HeapCountFor(int writes) {
            var vars = Enumerable.Range(0, writes).Select(i => new Variable($"v{i}")).ToArray();
            var nodes = vars
                .Select(v => (Node)new Assignment(v, new TypeCast(new Constant(true), TypeReference.To<bool?>())))
                .ToArray();
            using var exec = Interpreter.Execute(Interpreter.Compile(new Block(nodes, vars)));
            return exec.State.Heap.Count;
        }

        // One shared `true` cell regardless of how many times it is written.
        await Assert.That(HeapCountFor(1)).IsEqualTo(HeapCountFor(8));
    }
}