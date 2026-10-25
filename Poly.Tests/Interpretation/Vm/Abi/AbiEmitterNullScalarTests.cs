using Poly.Interpretation;
using Poly.Interpretation.Vm;
using Poly.Interpretation.Vm.Abi;
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

    [Test]
    public async Task RequiredScalar_NotEqualNull_IsTrue() {
        var age = new Parameter("age", TypeReference.To<long>());
        await Assert.That(Run(new NotEqual(age, new Constant(null)), 41L)).IsEqualTo(1L);
    }

    /// <summary>Zero is a value, not the null sentinel: the answer is folded, so the
    /// word is never tested against the sentinel.</summary>
    [Test]
    public async Task RequiredScalar_Zero_NotEqualNull_IsTrue() {
        var age = new Parameter("age", TypeReference.To<long>());
        await Assert.That(Run(new NotEqual(age, new Constant(null)), 0L)).IsEqualTo(1L);
    }

    /// <summary>An unseeded slot is still a scalar slot, not a null: the fold answers
    /// from the static type, so omission cannot turn a required scalar into null.</summary>
    [Test]
    public async Task RequiredScalar_Unseeded_NotEqualNull_IsTrue() {
        var age = new Parameter("age", TypeReference.To<long>());
        await Assert.That(Run(new NotEqual(age, new Constant(null)))).IsEqualTo(1L);
    }

    [Test]
    public async Task RequiredScalar_EqualNull_IsFalse() {
        var age = new Parameter("age", TypeReference.To<long>());
        await Assert.That(Run(new Equal(age, new Constant(null)), 41L)).IsEqualTo(0L);
    }

    [Test]
    public async Task RequiredScalar_NullOnLeft_NotEqual_IsTrue() {
        var age = new Parameter("age", TypeReference.To<long>());
        await Assert.That(Run(new NotEqual(new Constant(null), age), 41L)).IsEqualTo(1L);
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

    /// <summary>The folded answer is a constant in the emitted body: no heap dereference
    /// of the scalar word.</summary>
    [Test]
    public async Task RequiredScalar_NotEqualNull_EmitsNoHeapDeref() {
        var age = new Parameter("age", TypeReference.To<long>());
        var built = AbiEmitter.Build(
            new NotEqual(age, new Constant(null)),
            Interpreter.Analyze(new NotEqual(age, new Constant(null))),
            CompilationMode.NoDebug);
        var scan = VmTree.Read(built.Expression);
        await Assert.That(scan.HeapDerefs.Count).IsEqualTo(0);
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

    [Test]
    public async Task OptionalScalar_UnaryMinus_Present_UsesValue() {
        var age = OptionalAge();
        await Assert.That(RunValue(new UnaryMinus(age), 41L)).IsEqualTo(-41L);
    }

    [Test]
    public async Task OptionalScalar_UnaryMinus_Absent_IsNull() {
        var age = OptionalAge();
        using var exec = Execute(new UnaryMinus(age));
        await Assert.That(exec.Result.Value).IsNull();
    }
}
