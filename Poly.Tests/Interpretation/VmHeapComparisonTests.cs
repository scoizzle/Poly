using Poly.Interpretation;

namespace Poly.Tests.Interpretation;

/// <summary>F22: VmHeapComparison DateTime/DateOnly/Guid ordering and mixed-type fail-loud.</summary>
public class VmHeapComparisonTests {
    [Test]
    public async Task LessThan_DateTime_UsesHeapComparison() {
        var node = new LessThan(
            new Constant(new DateTime(2020, 1, 1)),
            new Constant(new DateTime(2021, 1, 1)));
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(1L);
    }

    [Test]
    public async Task Equal_Guid_UsesHeapComparison() {
        var g = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        using var exec = Interpreter.Execute(Interpreter.Compile(new Equal(new Constant(g), new Constant(g))));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(1L);
    }

    [Test]
    public async Task LessThan_DateOnly_UsesHeapComparison() {
        var node = new LessThan(
            new Constant(new DateOnly(2026, 1, 1)),
            new Constant(new DateOnly(2026, 6, 1)));
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(1L);
    }

    [Test]
    public async Task LessThan_MixedRuntimeTypes_FailsLoud() {
        // When analysis allows, runtime VmHeapComparison throws; mixed DateOnly/string rejected at compile.
        await Assert.That(() => Interpreter.Compile(
            new LessThan(new Constant(new DateOnly(2026, 1, 1)), new Constant("x"))
        )).Throws<InvalidOperationException>();
    }
}
