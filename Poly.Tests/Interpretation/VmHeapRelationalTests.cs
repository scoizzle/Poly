using Poly.Interpretation;

namespace Poly.Tests.Interpretation;

/// <summary>
/// VM relational/equality on heap-resident values — exercises
/// <c>VmHeapComparison</c> / Object.Equals paths in DirectVmAbiEmitter
/// (compare boxed values, not raw handles).
/// </summary>
public class VmHeapRelationalTests {
    [Test]
    public async Task LessThan_DateOnly_Ordered_IsTrue() {
        var node = new LessThan(
            new Constant(new DateOnly(2026, 1, 1)),
            new Constant(new DateOnly(2026, 9, 5)));
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(1L);
    }

    [Test]
    public async Task GreaterThan_DateOnly_Reversed_IsFalse() {
        var node = new GreaterThan(
            new Constant(new DateOnly(2026, 1, 1)),
            new Constant(new DateOnly(2026, 9, 5)));
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(0L);
    }

    [Test]
    public async Task Equal_DateOnly_Same_IsTrue() {
        var d = new DateOnly(2026, 9, 5);
        var node = new Equal(new Constant(d), new Constant(d));
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(1L);
    }

    [Test]
    public async Task LessThan_Strings_Lexicographic_IsTrue() {
        var node = new LessThan(new Constant("alpha"), new Constant("beta"));
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(1L);
    }

    [Test]
    public async Task GreaterThanOrEqual_Strings_Equal_IsTrue() {
        var node = new GreaterThanOrEqual(new Constant("poly"), new Constant("poly"));
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(1L);
    }

    [Test]
    public async Task Equal_Guid_Same_IsTrue() {
        var g = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var node = new Equal(new Constant(g), new Constant(g));
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(1L);
    }

    [Test]
    public async Task NotEqual_Guid_Different_IsTrue() {
        var node = new NotEqual(
            new Constant(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee")),
            new Constant(Guid.Parse("11111111-2222-3333-4444-555555555555")));
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(1L);
    }

    [Test]
    public async Task LessThan_MixedDateKinds_CompileRejects() {
        // DateOnly vs DateTime are both Cat.Date but not Compatible unless same CLR type / timestamps.
        await Assert.That(() => Interpreter.Compile(
            new LessThan(
                new Constant(new DateOnly(2026, 1, 1)),
                new Constant(new DateTime(2026, 1, 1)))
        )).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task LessThan_DateTime_Ordered_IsTrue() {
        var node = new LessThan(
            new Constant(new DateTime(2026, 1, 1)),
            new Constant(new DateTime(2026, 9, 5)));
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(1L);
    }

    [Test]
    public async Task Equal_DateTime_Same_IsTrue() {
        var d = new DateTime(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc);
        var node = new Equal(new Constant(d), new Constant(d));
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(1L);
    }

    [Test]
    public async Task LessThan_Guid_Ordered_IsTrue() {
        var a = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var b = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var node = new LessThan(new Constant(a), new Constant(b));
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(1L);
    }

    [Test]
    public async Task Equal_MixedIncomparableTypes_CompileRejects() {
        await Assert.That(() => Interpreter.Compile(
            new Equal(new Constant(Guid.NewGuid()), new Constant("x"))
        )).Throws<InvalidOperationException>()
            .WithMessageContaining("incompatible");
    }
}
