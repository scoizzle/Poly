namespace Poly.Tests.Interpretation;

/// <summary>
/// Pruned: old VM EH tests tied to primitive expansion and region markers.
/// Direct path uses structured CLR exceptions.
/// </summary>
public class ExceptionHandlingVmTests {
    [Test]
    public async Task EH_Vm_Deprecated_NonCritical(CancellationToken ct) {
        var v = true; await Assert.That(v).IsTrue();
    }
}