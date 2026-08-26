using Poly.Interpretation;
using Poly.Introspection;

namespace Poly.Tests.Interpretation;

/// <summary>
/// Stored-closure upvalue cells: late-bind, sharing, ABI words that are not
/// <c>long</c> literals, capture+args, parameters, loop/foreach, re-invoke.
/// </summary>
public class ClosureCaptureTests {
    [Test]
    public async Task Stored_CaptureAndArgs_MutateOffset_SeesLatest() {
        var offset = new Variable("offset");
        var fn = new Variable("fn");
        var x = new Parameter("x", TypeReference.To<long>());
        var node = new Block([
            new Assignment(offset, new Constant(10L)),
            new Assignment(fn, new Lambda([x], new Add(x, offset))),
            new Assignment(offset, new Constant(20L)),
            new Invoke(fn, new Constant(32L))
        ], [offset, fn]);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.RawValue).IsEqualTo(52L);
    }

    [Test]
    public async Task Stored_StringCapture_MutateAfterStore_SeesLatest() {
        var captured = new Variable("captured");
        var fn = new Variable("fn");
        var node = new Block([
            new Assignment(captured, new Constant("a")),
            new Assignment(fn, new Lambda([], captured)),
            new Assignment(captured, new Constant("b")),
            new Invoke(fn)
        ], [captured, fn]);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<string>()).IsEqualTo("b");
    }

    [Test]
    public async Task Stored_BoolCapture_MutateAfterStore_SeesLatest() {
        var captured = new Variable("captured");
        var fn = new Variable("fn");
        var node = new Block([
            new Assignment(captured, new Constant(false)),
            new Assignment(fn, new Lambda([], captured)),
            new Assignment(captured, new Constant(true)),
            new Invoke(fn)
        ], [captured, fn]);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<bool>()).IsTrue();
    }

    [Test]
    public async Task Stored_DoubleCapture_MutateAfterStore_SeesLatest() {
        var captured = new Variable("captured");
        var fn = new Variable("fn");
        var node = new Block([
            new Assignment(captured, new Constant(1.5)),
            new Assignment(fn, new Lambda([], captured)),
            new Assignment(captured, new Constant(2.5)),
            new Invoke(fn)
        ], [captured, fn]);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<double>()).IsEqualTo(2.5);
    }

    [Test]
    public async Task Stored_DecimalCapture_MutateAfterStore_SeesLatest() {
        var captured = new Variable("captured");
        var fn = new Variable("fn");
        var node = new Block([
            new Assignment(captured, new Constant(1.5m)),
            new Assignment(fn, new Lambda([], captured)),
            new Assignment(captured, new Constant(2.5m)),
            new Invoke(fn)
        ], [captured, fn]);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<decimal>()).IsEqualTo(2.5m);
    }

    [Test]
    public async Task Stored_TwoCaptures_AddAfterMutate() {
        var a = new Variable("a");
        var b = new Variable("b");
        var fn = new Variable("fn");
        var node = new Block([
            new Assignment(a, new Constant(1L)),
            new Assignment(b, new Constant(2L)),
            new Assignment(fn, new Lambda([], new Add(a, b))),
            new Assignment(a, new Constant(10L)),
            new Assignment(b, new Constant(20L)),
            new Invoke(fn)
        ], [a, b, fn]);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.RawValue).IsEqualTo(30L);
    }

    [Test]
    public async Task Stored_OuterParameterCapture_ReturnsSetArg() {
        var outer = new Parameter("outer", TypeReference.To<long>());
        var fn = new Variable("fn");
        var node = new Block([
            new Assignment(fn, new Lambda([], outer)),
            new Invoke(fn)
        ], [fn]);
        var program = Interpreter.Compile(node);
        using var exec = Interpreter.Execute(program, s => s.SetArgs(7L));
        await Assert.That(exec.RawValue).IsEqualTo(7L);
    }

    [Test]
    public async Task Stored_NestedLambdaCapturesOuterParameter() {
        var p = new Parameter("p", TypeReference.To<long>());
        var inner = new Variable("inner");
        var resultFn = new Variable("resultFn");
        var outer = new Lambda([p], new Block([
            new Assignment(inner, new Lambda([], p)),
            inner
        ], [inner]));
        var node = new Block([
            new Assignment(resultFn, new Invoke(outer, new Constant(9L))),
            new Invoke(resultFn)
        ], [resultFn]);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.RawValue).IsEqualTo(9L);
    }

    [Test]
    public async Task Stored_InvokeTwice_WriteBetween_SeesEachValue() {
        var captured = new Variable("captured");
        var fn = new Variable("fn");
        var first = new Variable("first");
        var node = new Block([
            new Assignment(captured, new Constant(1L)),
            new Assignment(fn, new Lambda([], captured)),
            new Assignment(first, new Invoke(fn)),
            new Assignment(captured, new Constant(2L)),
            new Add(first, new Invoke(fn))
        ], [captured, fn, first]);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.RawValue).IsEqualTo(3L);
    }

    [Test]
    public async Task Stored_IfFalseDoesNotCreate_OuterReadStillWorks() {
        var captured = new Variable("captured");
        var fn = new Variable("fn");
        var node = new Block([
            new Assignment(captured, new Constant(1L)),
            new IfStatement(
                new Constant(false),
                new Assignment(fn, new Lambda([], captured))),
            new Assignment(captured, new Constant(2L)),
            captured
        ], [captured, fn]);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.RawValue).IsEqualTo(2L);
    }

    [Test]
    public async Task Stored_IfTrueCreates_MutateThenInvoke() {
        var captured = new Variable("captured");
        var fn = new Variable("fn");
        var node = new Block([
            new Assignment(captured, new Constant(1L)),
            new IfStatement(
                new Constant(true),
                new Assignment(fn, new Lambda([], captured))),
            new Assignment(captured, new Constant(2L)),
            new Invoke(fn)
        ], [captured, fn]);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.RawValue).IsEqualTo(2L);
    }

    [Test]
    public async Task Stored_LoopCreatedClosures_AllSeeFinalI() {
        var i = new Variable("i");
        var f0 = new Variable("f0");
        var f1 = new Variable("f1");
        var f2 = new Variable("f2");
        var node = new Block([
            new Assignment(i, new Constant(0L)),
            new Assignment(f0, new Lambda([], i)),
            new Assignment(i, new Add(i, new Constant(1L))),
            new Assignment(f1, new Lambda([], i)),
            new Assignment(i, new Add(i, new Constant(1L))),
            new Assignment(f2, new Lambda([], i)),
            new Assignment(i, new Add(i, new Constant(1L))),
            new Add(new Invoke(f0), new Add(new Invoke(f1), new Invoke(f2)))
        ], [i, f0, f1, f2]);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.RawValue).IsEqualTo(9L);
    }

    [Test]
    public async Task Stored_ForeachLoopVariable_LastValue() {
        var item = new Variable("item");
        var fn = new Variable("fn");
        var node = new Block([
            new ForEachLoop(
                item,
                new Constant(new long[] { 1L, 2L, 3L }),
                new Assignment(fn, new Lambda([], item))),
            new Invoke(fn)
        ], [fn]);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.RawValue).IsEqualTo(3L);
    }

    [Test]
    public async Task Stored_DeclareInitVariable_MutateAfterStore() {
        var captured = new Variable("captured");
        var fn = new Variable("fn");
        var node = new Block([
            new Assignment(captured, new Constant(1L)),
            new Assignment(fn, new Lambda([], captured)),
            new Assignment(captured, new Constant(2L)),
            new Invoke(fn)
        ], [captured, fn]);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.RawValue).IsEqualTo(2L);
    }

    [Test]
    public async Task NestedStoredClosure_GetValue_IsInnerBody() {
        var captured = new Variable("captured");
        var inner = new Variable("inner");
        var outer = new Variable("outer");
        var resultFn = new Variable("resultFn");
        var node = new Block([
            new Assignment(captured, new Constant(1L)),
            new Assignment(outer, new Lambda([], new Block([
                new Assignment(inner, new Lambda([], captured)),
                inner
            ], [inner]))),
            new Assignment(captured, new Constant(4L)),
            new Assignment(resultFn, new Invoke(outer)),
            new Invoke(resultFn)
        ], [captured, outer, resultFn]);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(4L);
    }

    [Test]
    public async Task Invoke_ParameterHoldingLambda_CallsThrough() {
        var x = new Parameter("x", TypeReference.To<long>());
        var add1 = new Lambda([x], new Add(x, new Constant(1L)));
        var f = new Parameter("f");
        var apply = new Lambda([f], new Invoke(f, new Constant(41L)));
        var node = new Invoke(apply, add1);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(42L);
    }

    [Test]
    public async Task NestedLambda_OwnBlockLocals_NotOuterCaptures() {
        var nestedLocal = new Variable("y");
        var inner = new Variable("inner");
        var outer = new Variable("outer");
        var node = new Block([
            new Assignment(outer, new Lambda([], new Block([
                new Assignment(inner, new Lambda([], new Block([
                    new Assignment(nestedLocal, new Constant(5L)),
                    nestedLocal
                ], [nestedLocal]))),
                new Invoke(inner)
            ], [inner]))),
            new Invoke(outer)
        ], [outer]);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(5L);
    }

    [Test]
    public async Task NestedLambda_OwnForeachLocal_NotOuterCapture() {
        var item = new Variable("item");
        var inner = new Variable("inner");
        var outer = new Variable("outer");
        var last = new Variable("last");
        var node = new Block([
            new Assignment(outer, new Lambda([], new Block([
                new Assignment(inner, new Lambda([], new Block([
                    new Assignment(last, new Constant(0L)),
                    new ForEachLoop(
                        item,
                        new Constant(new long[] { 1L, 2L, 3L }),
                        new Assignment(last, item)),
                    last
                ], [last]))),
                new Invoke(inner)
            ], [inner]))),
            new Invoke(outer)
        ], [outer]);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(3L);
    }

    [Test]
    public async Task Invoke_LambdaBlock_ReturnThenComment_YieldsReturnValue() {
        var lambda = new Lambda([], new Block([
            new IfStatement(new Constant(true), new Return(new Constant(7L))),
            new Comment("x")
        ]));
        var node = new Invoke(lambda);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(7L);
    }

    [Test]
    public async Task StoredHof_InvokeVariable_BindsCalleeAndBoolResult() {
        var x = new Parameter("x", TypeReference.To<bool>());
        var notX = new Lambda([x], new Not(x));
        var f = new Parameter("f");
        var apply = new Lambda([f], new Invoke(f, new Constant(false)));
        var applyVar = new Variable("applyVar");
        var node = new Block([
            new Assignment(applyVar, apply),
            new Invoke(applyVar, notX)
        ], [applyVar]);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<bool>()).IsTrue();
    }

    [Test]
    public async Task InnerDeclareInit_SameNameAsOuter_IsOwnLocal() {
        var outer = new Variable("x");
        var inner = new Variable("x");
        var fn = new Variable("fn");
        var node = new Block([
            new Assignment(outer, new Constant(9L)),
            new Assignment(fn, new Lambda([], new Block([
                new Assignment(inner, new Constant(1L)),
                inner
            ], [inner]))),
            new Invoke(fn)
        ], [outer, fn]);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(1L);
    }
}