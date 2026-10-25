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
    private static long Run(Node body, params object?[] args) {
        var program = Interpreter.Compile(body);
        using var exec = Interpreter.Execute(program, s => s.SetArgs(args));
        return exec.RawValue;
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
}
