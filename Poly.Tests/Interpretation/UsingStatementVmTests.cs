using Poly.Interpretation;

namespace Poly.Tests.Interpretation;

/// <summary>F17: Using non-IDisposable and nested using.</summary>
public class UsingStatementVmTests {
    [Test]
    public async Task Using_NonIDisposable_SkipsDispose_Completes() {
        // Product: Dispose only when resource is IDisposable (IfThen TypeIs) — non-disposable skips.
        var node = new UsingStatement(new Constant("not-disposable"), new Constant(42L));
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        // Product skips Dispose when resource is not IDisposable; using body is void-coerced.
        await Assert.That(exec.Result.IsVoid).IsTrue();
    }

    [Test]
    public async Task Using_Nested_DisposesInnerThenOuter() {
        var outer = new TrackingDisposable();
        var inner = new TrackingDisposable();
        var node = new UsingStatement(
            new Constant(outer),
            new UsingStatement(new Constant(inner), new Constant(1L)));
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(inner.Disposed).IsTrue();
        await Assert.That(outer.Disposed).IsTrue();
    }

    private sealed class TrackingDisposable : IDisposable {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }
}
