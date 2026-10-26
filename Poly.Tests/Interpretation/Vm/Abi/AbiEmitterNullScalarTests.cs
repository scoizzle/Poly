using Poly.Interpretation;
using Poly.Introspection;

namespace Poly.Tests.Interpretation.Vm.Abi;

/// <summary>
/// Null tests over scalar parameters (#330). A required scalar holds its value in the
/// slot, so it can never be null: the compare folds at compile time and never derefs
/// the word as a handle. An optional scalar is heap-resident (<c>Nullable&lt;T&gt;</c>),
/// so the VM's null sentinel decides present vs absent and a present zero stays
/// present.
/// </summary>
public class AbiEmitterNullScalarTests {
    private static ExecutionResult Execute(Node body, params object?[] args) {
        var program = Interpreter.Compile(body);
        return Interpreter.Execute(program, s => s.SetArgs(args));
    }

    private static long Run(Node body, params object?[] args) {
        using var exec = Execute(body, args);
        return exec.RawValue;
    }

    private static long RunValue(Node body, params object?[] args) {
        using var exec = Execute(body, args);
        return exec.GetValue<long>();
    }

    /// <summary>Folding the null test folds the test only: the scalar operand's own
    /// effects still run. The store inside the comparison must not disappear with the
    /// null check.</summary>
    [Test]
    public async Task RequiredScalar_SideEffectingOperand_EffectStillRuns() {
        var x = new Variable("x");
        var seen = new Variable("seen");
        Node body = new Block([
            new Assignment(x, new Constant(0L)),
            new Assignment(seen, new NotEqual(
                new Assignment(x, new Constant(42L)), new Constant(null))),
            new Variable("x"),
        ], [x, seen]);
        await Assert.That(Run(body)).IsEqualTo(42L);
    }

    private static Parameter OptionalAge() =>
        new("age", new OptionalTypeReference(TypeReference.To<long>()));

    [Test]
    public async Task OptionalScalar_Present_NotEqualNull_IsTrue() {
        await Assert.That(Run(new NotEqual(OptionalAge(), new Constant(null)), 41L)).IsEqualTo(1L);
    }

    /// <summary>Present zero is a heap value, not the sentinel: it must not read as
    /// absent.</summary>
    [Test]
    public async Task OptionalScalar_PresentZero_NotEqualNull_IsTrue() {
        await Assert.That(Run(new NotEqual(OptionalAge(), new Constant(null)), 0L)).IsEqualTo(1L);
    }

    [Test]
    public async Task OptionalScalar_Absent_NotEqualNull_IsFalse() {
        await Assert.That(Run(new NotEqual(OptionalAge(), new Constant(null)))).IsEqualTo(0L);
    }

    [Test]
    public async Task OptionalScalar_Absent_EqualNull_IsTrue() {
        await Assert.That(Run(new Equal(OptionalAge(), new Constant(null)))).IsEqualTo(1L);
    }

    [Test]
    public async Task OptionalScalar_PresentZero_EqualNull_IsFalse() {
        await Assert.That(Run(new Equal(OptionalAge(), new Constant(null)), 0L)).IsEqualTo(0L);
    }

    /// <summary>A nullable CLR type spells the same optional scalar; the slot is a heap
    /// handle, so absence is the sentinel.</summary>
    [Test]
    public async Task OptionalScalar_NullableClrType_PresentZero_NotEqualNull_IsTrue() {
        var age = new Parameter("age", TypeReference.To<long?>());
        await Assert.That(Run(new NotEqual(age, new Constant(null)), 0L)).IsEqualTo(1L);
    }

    [Test]
    public async Task OptionalScalar_NullableClrType_Absent_EqualNull_IsTrue() {
        var age = new Parameter("age", TypeReference.To<long?>());
        await Assert.That(Run(new Equal(age, new Constant(null)))).IsEqualTo(1L);
    }

    /// <summary>Arithmetic unboxes the present value; an absent operand makes the
    /// lifted result absent rather than operating on the handle.</summary>
    [Test]
    public async Task OptionalScalar_AddValue_Present_UsesValue() {
        var age = OptionalAge();
        await Assert.That(RunValue(new Add(age, new Constant(1L)), 41L)).IsEqualTo(42L);
    }

    [Test]
    public async Task OptionalScalar_AddValue_PresentZero_UsesZero() {
        var age = OptionalAge();
        await Assert.That(RunValue(new Add(age, new Constant(1L)), 0L)).IsEqualTo(1L);
    }

    [Test]
    public async Task OptionalScalar_AddValue_Absent_IsNull() {
        var age = OptionalAge();
        using var exec = Execute(new Add(age, new Constant(1L)));
        await Assert.That(exec.Result.Value).IsNull();
    }

    /// <summary>Value equality unboxes too: present zero equals zero, and an absent
    /// operand is equal only to another absent one.</summary>
    [Test]
    public async Task OptionalScalar_EqualValue_PresentZero_IsTrue() {
        var age = OptionalAge();
        await Assert.That(Run(new Equal(age, new Constant(0L)), 0L)).IsEqualTo(1L);
    }

    [Test]
    public async Task OptionalScalar_EqualValue_Absent_IsFalse() {
        var age = OptionalAge();
        await Assert.That(Run(new Equal(age, new Constant(0L)))).IsEqualTo(0L);
    }

    [Test]
    public async Task OptionalScalar_NotEqualValue_Absent_IsTrue() {
        var age = OptionalAge();
        await Assert.That(Run(new NotEqual(age, new Constant(0L)))).IsEqualTo(1L);
    }

    /// <summary>A value constant is a value, not a heap handle: a large one must not be
    /// dereferenced off the heap.</summary>
    [Test]
    public async Task OptionalScalar_EqualLargeValue_Present_IsFalse() {
        var age = OptionalAge();
        await Assert.That(Run(new Equal(age, new Constant(999_999L)), 41L)).IsEqualTo(0L);
    }

    [Test]
    public async Task OptionalScalar_Relational_Present_UsesValue() {
        var age = OptionalAge();
        await Assert.That(Run(new GreaterThanOrEqual(age, new Constant(0L)), 0L)).IsEqualTo(1L);
        await Assert.That(Run(new LessThan(age, new Constant(10L)), 41L)).IsEqualTo(0L);
    }

    [Test]
    public async Task OptionalScalar_Relational_Absent_IsFalse() {
        var age = OptionalAge();
        await Assert.That(Run(new GreaterThanOrEqual(age, new Constant(0L)))).IsEqualTo(0L);
    }

    /// <summary>A local that holds a nullable parameter is a heap handle too: it is
    /// boxed on assignment and unboxed on a value operation.</summary>
    [Test]
    public async Task OptionalScalar_LocalAlias_AddValue_UsesValue() {
        var age = OptionalAge();
        var local = new Variable("local");
        var body = new Block([
            new Assignment(local, age),
            new Add(local, new Constant(1L))
        ], [local]);
        await Assert.That(RunValue(body, 41L)).IsEqualTo(42L);
    }

    [Test]
    public async Task OptionalScalar_LocalAlias_EqualNull_UsesSentinel() {
        var age = OptionalAge();
        var local = new Variable("local");
        var body = new Block([
            new Assignment(local, age),
            new Equal(local, new Constant(null))
        ], [local]);
        await Assert.That(Run(body, 41L)).IsEqualTo(0L);
        await Assert.That(Run(body)).IsEqualTo(1L);
    }

    /// <summary>A boxed narrower argument (int for a long? slot) unboxes at the
    /// declared underlying type, not by a strict CLR cast.</summary>
    [Test]
    public async Task OptionalScalar_SetArgsBoxedInt_AddValue_UsesValue() {
        var age = OptionalAge();
        await Assert.That(RunValue(new Add(age, new Constant(1L)), 41)).IsEqualTo(42L);
    }

    /// <summary>Nullable bool compares through its 0/1 value word.</summary>
    [Test]
    public async Task OptionalScalar_Bool_EqualTrue_Present_IsTrue() {
        var flag = new Parameter("flag", new OptionalTypeReference(TypeReference.To<bool>()));
        await Assert.That(Run(new Equal(flag, new Constant(true)), true)).IsEqualTo(1L);
    }

    [Test]
    public async Task OptionalScalar_Bool_EqualTrue_Absent_IsFalse() {
        var flag = new Parameter("flag", new OptionalTypeReference(TypeReference.To<bool>()));
        await Assert.That(Run(new Equal(flag, new Constant(true)))).IsEqualTo(0L);
    }

    [Test]
    public async Task OptionalScalar_Bool_NotEqualTrue_Absent_IsTrue() {
        var flag = new Parameter("flag", new OptionalTypeReference(TypeReference.To<bool>()));
        await Assert.That(Run(new NotEqual(flag, new Constant(true)))).IsEqualTo(1L);
    }

    private sealed record OptionalBag(long? Limit);

    private static Node BagLimit(long? limit) =>
        new Member(new Constant(new OptionalBag(limit)), "Limit");

    /// <summary>A nullable member read is a heap handle like a nullable parameter:
    /// present compares and computes by value, absent is the sentinel.</summary>
    [Test]
    public async Task OptionalMember_Value_EqualValue_UsesValue() {
        await Assert.That(Run(new Equal(BagLimit(41), new Constant(41L)))).IsEqualTo(1L);
    }

    [Test]
    public async Task OptionalMember_AddValue_Present_UsesValue() {
        await Assert.That(RunValue(new Add(BagLimit(41), new Constant(1L)))).IsEqualTo(42L);
    }

    /// <summary>A cast to an optional type boxes the underlying value; null stays the
    /// sentinel.</summary>
    [Test]
    public async Task OptionalScalar_CastFromScalar_BoxesValue() {
        var body = new TypeCast(new Constant(41L), new OptionalTypeReference(TypeReference.To<long>()));
        using var exec = Execute(body);
        await Assert.That(exec.GetValue<long>()).IsEqualTo(41L);
    }

    [Test]
    public async Task OptionalScalar_CastFromNull_IsNull() {
        var body = new TypeCast(new Constant(null), new OptionalTypeReference(TypeReference.To<long>()));
        using var exec = Execute(body);
        await Assert.That(exec.Result.Value).IsNull();
    }

    /// <summary>Three-valued logic has no bool result here, so it fails closed.</summary>
    [Test]
    public async Task OptionalScalar_LogicalNot_IsCompileRejected() {
        var flag = new Parameter("flag", new OptionalTypeReference(TypeReference.To<bool>()));
        await Assert.That(() => Interpreter.Compile(new Not(flag)))
            .Throws<InvalidOperationException>();
    }

    private sealed class NullableCtor {
        public NullableCtor(long? value) => Value = value;
        public long? Value { get; }
    }

    [Test]
    public async Task NullableCtorParam_ScalarArgument_Boxes() {
        using var exec = Execute(new New(TypeReference.To<NullableCtor>(), new Constant(41L)));
        await Assert.That(exec.GetValue<NullableCtor>()!.Value).IsEqualTo(41L);
    }
}