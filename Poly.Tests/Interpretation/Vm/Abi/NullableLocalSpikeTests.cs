using Poly.Interpretation;

namespace Poly.Tests.Interpretation.Vm.Abi;

/// <summary>
/// An optional value type is a <c>Nullable&lt;T&gt;</c> local. The frame keeps the
/// heap sentinel: absent is handle 0, present zero is a boxed cell.
/// </summary>
public class NullableLocalSpikeTests {
    private static ExecutionResult Run(Node body, params object?[] args) =>
        Interpreter.Execute(Interpreter.Compile(body), s => s.SetArgs(args));

    [Test]
    public async Task OptionalInt_PlusOne_Present_IsSix() {
        var n = new Parameter("n", new OptionalTypeReference(TypeReference.To<int>()));
        using var exec = Run(new Add(n, new Constant(1)), 5);
        await Assert.That(exec.GetValue<int>()).IsEqualTo(6);
    }

    [Test]
    public async Task OptionalInt_PlusOne_Absent_IsNull() {
        var n = new Parameter("n", new OptionalTypeReference(TypeReference.To<int>()));
        using var exec = Run(new Add(n, new Constant(1)));
        await Assert.That(exec.Result.Value).IsNull();
    }

    [Test]
    public async Task OptionalInt_PresentZero_PlusOne_IsOne() {
        var n = new Parameter("n", new OptionalTypeReference(TypeReference.To<int>()));
        using var exec = Run(new Add(n, new Constant(1)), 0);
        await Assert.That(exec.GetValue<int>()).IsEqualTo(1);
    }

    [Test]
    public async Task OptionalInt_Absent_EqualZero_IsFalse() {
        var n = new Parameter("n", new OptionalTypeReference(TypeReference.To<int>()));
        using var exec = Run(new Equal(n, new Constant(0)));
        await Assert.That(exec.GetValue<bool>()).IsFalse();
    }

    [Test]
    public async Task OptionalInt_BothAbsent_Equal_IsTrue() {
        var left = new Parameter("left", new OptionalTypeReference(TypeReference.To<int>()));
        var right = new Parameter("right", new OptionalTypeReference(TypeReference.To<int>()));
        using var exec = Run(new Equal(left, right));
        await Assert.That(exec.GetValue<bool>()).IsTrue();
    }

    [Test]
    public async Task OptionalInt_PlusLong_PromotesToInt64() {
        var n = new Parameter("n", new OptionalTypeReference(TypeReference.To<int>()));
        var wide = new Parameter("wide", TypeReference.To<long>());
        using var exec = Run(new Add(n, wide), int.MaxValue, 1L);
        await Assert.That(exec.GetValue<long>()).IsEqualTo(2147483648L);
    }

    [Test]
    public async Task OptionalInt_PlusOptionalLong_PromotesToInt64() {
        var n = new Parameter("n", new OptionalTypeReference(TypeReference.To<int>()));
        var wide = new Parameter("wide", new OptionalTypeReference(TypeReference.To<long>()));
        using var exec = Run(new Add(n, wide), int.MaxValue, 1L);
        await Assert.That(exec.GetValue<long>()).IsEqualTo(2147483648L);
    }

    [Test]
    public async Task Double_PlusOptionalInt_PromotesToDouble() {
        var d = new Parameter("d", TypeReference.To<double>());
        var n = new Parameter("n", new OptionalTypeReference(TypeReference.To<int>()));
        using var exec = Run(new Add(d, n), 0.5d, 1);
        await Assert.That(exec.GetValue<double>()).IsEqualTo(1.5d);
    }

    [Test]
    public async Task OptionalShort_PlusOptionalShort_PromotesToInt32() {
        var left = new Parameter("left", new OptionalTypeReference(TypeReference.To<short>()));
        var right = new Parameter("right", new OptionalTypeReference(TypeReference.To<short>()));
        using var exec = Run(new Add(left, right), (short)30000, (short)30000);
        await Assert.That(exec.GetValue<int>()).IsEqualTo(60000);
    }

    [Test]
    public async Task OptionalFloat_PlusDouble_PromotesToDouble() {
        var f = new Parameter("f", new OptionalTypeReference(TypeReference.To<float>()));
        var d = new Parameter("d", TypeReference.To<double>());
        using var exec = Run(new Add(f, d), 1f, double.MaxValue);
        await Assert.That(exec.Result.Value).IsEqualTo(double.MaxValue);
    }

    [Test]
    public async Task OptionalInt_PlusLong_Absent_IsNull() {
        var n = new Parameter("n", new OptionalTypeReference(TypeReference.To<int>()));
        var wide = new Parameter("wide", TypeReference.To<long>());
        using var exec = Run(new Add(n, wide), null, 1L);
        await Assert.That(exec.Result.Value).IsNull();
    }

    [Test]
    public async Task OptionalInt_LessThanLong_Promotes() {
        var n = new Parameter("n", new OptionalTypeReference(TypeReference.To<int>()));
        var wide = new Parameter("wide", TypeReference.To<long>());
        using var exec = Run(new LessThan(n, wide), int.MaxValue, 2147483648L);
        await Assert.That(exec.GetValue<bool>()).IsTrue();
    }

    [Test]
    public async Task OptionalShort_LeftShift_PromotesToInt32() {
        var n = new Parameter("n", new OptionalTypeReference(TypeReference.To<short>()));
        using var exec = Run(new ShiftLeft(n, new Constant(1)), (short)20000);
        await Assert.That(exec.GetValue<int>()).IsEqualTo(40000);
    }

    [Test]
    public async Task OptionalShort_LeftShift_Absent_IsNull() {
        var n = new Parameter("n", new OptionalTypeReference(TypeReference.To<short>()));
        using var exec = Run(new ShiftLeft(n, new Constant(1)));
        await Assert.That(exec.Result.Value).IsNull();
    }
}