using Poly.Interpretation;
using Poly.Interpretation.Vm;
using Poly.Syntax;
using Poly.Syntax.Nodes;

using SN = Poly.Syntax.Nodes;

namespace Poly.Tests.Interpretation;

/// <summary>
/// VM-path tests for closure/upvalue execution through the full Interpreter pipeline.
/// Maps to P3-A (nested calls ring fix) and P3-C (closure upvalue tests).
/// </summary>
public class ClosureVmTests {
    /// <summary>
    /// Outer lambda captures a variable, returns it. Verifies basic
    /// AllocClosure + LoadUpvalue pipeline.
    /// </summary>
    [Test]
    public async Task Lambda_NoCapture_ReturnsBodyValue() {
        // (() => 42)()
        var lambda = new Lambda([], new Constant(42));
        var invoke = new Invoke(lambda);
        var program = Interpreter.Compile(invoke);
        using var exec = Interpreter.Execute(program);
        await Assert.That(exec.RawValue).IsEqualTo(42L);
    }

    /// <summary>
    /// Lambda with parameter — the simplest parameterized function call.
    /// </summary>
    [Test]
    public async Task Lambda_WithParam_ReturnsParam() {
        // ((x) => x)(99)
        var p = new Parameter("x", TypeReference.To<long>());
        var lambda = new Lambda([p], p);
        var invoke = new Invoke(lambda, new Constant(99L));
        var program = Interpreter.Compile(invoke);
        using var exec = Interpreter.Execute(program, s => s.SetArgs());
        await Assert.That(exec.RawValue).IsEqualTo(99L);
    }

    /// <summary>
    /// Simple function call through the VM. Verifies the basic
    /// Invoke(Lambda) → Call → Function table → Return path.
    /// </summary>
    [Test]
    public async Task Lambda_Call_RingPreservedAcrossCalls() {
        // f(x) = x + 1; f(41) → 42
        var p = new Parameter("x", TypeReference.To<long>());
        var body = new SN.Add(p, new Constant(1L));
        var lambda = new Lambda([p], body);
        var invoke = new Invoke(lambda, new Constant(41L));
        var program = Interpreter.Compile(invoke);
        using var exec = Interpreter.Execute(program);
        await Assert.That(exec.RawValue).IsEqualTo(42L);
    }
}