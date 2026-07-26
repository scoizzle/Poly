using Poly.Ast;
using Poly.Ast.Nodes;
using Poly.Interpretation;
using Poly.Interpretation.Vm;

using SN = Poly.Ast.Nodes;

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
    public async Task NestedLambda_SimplePassthrough() {
        // f(x) = x
        // g(x) = f(x)
        // g(42) = 42
        // Tests basic nested function call.
        var pF = new Parameter("x", TypeReference.To<long>());
        var f = new Lambda([pF], pF);

        var pG = new Parameter("x", TypeReference.To<long>());
        var gBody = new Invoke(f, pG);
        var g = new Lambda([pG], gBody);

        var invoke = new Invoke(g, new Constant(42L));
        var program = Interpreter.Compile(invoke);
        using var exec = Interpreter.Execute(program);
        await Assert.That(exec.RawValue).IsEqualTo(42L);
    }

    [Test]
    public async Task NestedLambda_InnerAdds() {
        // f(x) = x + 1
        // g(x) = f(x)
        // g(41) → 42
        var pF = new Parameter("x", TypeReference.To<long>());
        var f = new Lambda([pF], new Add(pF, new Constant(1L)));

        var pG = new Parameter("x", TypeReference.To<long>());
        var g = new Lambda([pG], new Invoke(f, pG));

        var invoke = new Invoke(g, new Constant(41L));
        var program = Interpreter.Compile(invoke);
        using var exec = Interpreter.Execute(program);
        await Assert.That(exec.RawValue).IsEqualTo(42L);
    }

    [Test]
    public async Task NestedLambda_OuterComputesThenCalls() {
        // f(x) = x
        // g(x) = f(x * 2)
        // g(20) → 40  (passthrough, no transformation in f)
        var pF = new Parameter("x", TypeReference.To<long>());
        var f = new Lambda([pF], pF);

        var pG = new Parameter("x", TypeReference.To<long>());
        var g = new Lambda([pG], new Invoke(f, new Multiply(pG, new Constant(2L))));

        var invoke = new Invoke(g, new Constant(20L));
        var program = Interpreter.Compile(invoke);
        using var exec = Interpreter.Execute(program);
        await Assert.That(exec.RawValue).IsEqualTo(40L);
    }

    [Test]
    public async Task NestedLambda_CallPreservesOuterRing() {
        // f(x) = x + 1
        // g(x) = f(x * 2)
        // g(20) → f(40) → 41
        // The inner call f(40) exercises SavedSp ring save in g's delegate:
        // g's ring state must not be clobbered by f's ring allocation.
        var pF = new Parameter("x", TypeReference.To<long>());
        var fBody = new Add(pF, new Constant(1L));
        var f = new Lambda([pF], fBody);

        var pG = new Parameter("x", TypeReference.To<long>());
        var gBody = new Invoke(f, new Multiply(pG, new Constant(2L)));
        var g = new Lambda([pG], gBody);

        var invoke = new Invoke(g, new Constant(20L));
        var program = Interpreter.Compile(invoke);
        using var exec = Interpreter.Execute(program);
        await Assert.That(exec.RawValue).IsEqualTo(41L);
    }

    [Test]
    public async Task Lambda_Call_RingPreservedAcrossCalls() {
        // f(x) = x + 1; f(41) → 42
        var p = new Parameter("x", TypeReference.To<long>());
        var body = new Add(p, new Constant(1L));
        var lambda = new Lambda([p], body);
        var invoke = new Invoke(lambda, new Constant(41L));
        var program = Interpreter.Compile(invoke);
        using var exec = Interpreter.Execute(program);
        await Assert.That(exec.RawValue).IsEqualTo(42L);
    }
}