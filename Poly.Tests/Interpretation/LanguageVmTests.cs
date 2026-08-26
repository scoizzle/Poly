using Poly.Interpretation;
using Poly.Introspection;

namespace Poly.Tests.Interpretation;

/// <summary>Language-VM oracles: Interpreter.Compile on Syntax nodes, no DomainModeling.</summary>
public class LanguageVmTests {
    [Test]
    public async Task Comment_AsProgram_IsVoid() {
        using var exec = Interpreter.Execute(Interpreter.Compile(new Comment("note")));
        await Assert.That(exec.Result.IsVoid).IsTrue();
    }

    [Test]
    public async Task Comment_AsValue_CompileRejected() {
        await Assert.That(() => Interpreter.Compile(new Add(new Comment("x"), new Constant(1L))))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Await_CompileRejected() {
        await Assert.That(() => Interpreter.Compile(new Await(new Constant(1L))))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ParameterReference_CompileRejected() {
        await Assert.That(() => Interpreter.Compile(new ParameterReference()))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task TypeCast_IntToDouble_BitcastResult() {
        var node = new TypeCast(new Constant(42), TypeReference.To<double>());
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<double>()).IsEqualTo(42.0);
    }

    [Test]
    public async Task TypeCast_DoubleToInt_Truncates() {
        var node = new TypeCast(new Constant(3.9), TypeReference.To<int>());
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<int>()).IsEqualTo(3);
    }

    [Test]
    public async Task TypeAs_MatchingReference_KeepsObject() {
        var node = new TypeAs(new Constant("hello"), TypeReference.To<object>());
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<string>()).IsEqualTo("hello");
    }

    [Test]
    public async Task TypeAs_Mismatch_IsNull() {
        var node = new TypeAs(new Constant("hello"), TypeReference.To<Uri>());
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<object>()).IsNull();
    }

    [Test]
    public async Task TypeOf_String_ReturnsRuntimeType() {
        var node = new TypeOf(TypeReference.To<string>());
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<Type>()).IsEqualTo(typeof(string));
    }

    [Test]
    public async Task Default_Untyped_IsZero() {
        using var exec = Interpreter.Execute(Interpreter.Compile(new Default()));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(0L);
    }

    [Test]
    public async Task Default_String_IsAbiNull() {
        using var exec = Interpreter.Execute(Interpreter.Compile(new Default(TypeReference.To<string>())));
        await Assert.That(exec.GetValue<object>()).IsNull();
    }

    [Test]
    public async Task ThrowExpression_ThrowsOperand() {
        var node = new ThrowExpression(new New(TypeReference.To<InvalidOperationException>()));
        await Assert.That(() => {
            using var exec = Interpreter.Execute(Interpreter.Compile(node));
        }).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Lambda_Invoke_AddsArguments() {
        var x = new Parameter("x", TypeReference.To<long>());
        var y = new Parameter("y", TypeReference.To<long>());
        var node = new Invoke(new Lambda([x, y], new Add(x, y)), new Constant(3L), new Constant(4L));
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(7L);
    }

    [Test]
    public async Task ClrMethod_ToString_OnConstantString() {
        var node = new Invoke(new Member(new Constant("hi"), "ToString"));
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<string>()).IsEqualTo("hi");
    }

    [Test]
    public async Task New_Exception_IsHeapObject() {
        var node = new New(TypeReference.To<InvalidOperationException>(), new Constant("boom"));
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        var ex = exec.GetValue<InvalidOperationException>();
        await Assert.That(ex).IsNotNull();
        await Assert.That(ex!.Message).IsEqualTo("boom");
    }

    [Test]
    public async Task NewArray_Object_IsHeapArray() {
        var node = new NewArray(TypeReference.To<object>(), new Constant(3L));
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        var arr = exec.GetValue<Array>();
        await Assert.That(arr).IsNotNull();
        await Assert.That(arr!.Length).IsEqualTo(3);
    }

    [Test]
    public async Task IndexAccess_Array_ReturnsElement() {
        var arr = new Variable("arr");
        var node = new Block([
            new Assignment(arr, new Constant(new long[] { 10, 20, 30 })),
            new IndexAccess(arr, new Constant(1))
        ], [arr]);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(20L);
    }

    [Test]
    public async Task Parameter_SetArgs_ReturnsArg() {
        var p = new Parameter("x", TypeReference.To<long>());
        var program = Interpreter.Compile(p);
        using var exec = Interpreter.Execute(program, s => s.SetArgs(9L));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(9L);
    }

    [Test]
    public async Task BitwiseAnd_Mask() {
        var node = new BitwiseAnd(new Constant(0b1100L), new Constant(0b1010L));
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(0b1000L);
    }

    [Test]
    public async Task Switch_MatchingCase_Only() {
        var taken = new Variable("taken");
        var node = new Block([
            new Assignment(taken, new Constant(0L)),
            new SwitchStatement(
                new Constant(2L),
                [new SwitchCase(new Constant(2L), new Assignment(taken, new Constant(1L)))],
                new Assignment(taken, new Constant(9L))),
            taken
        ], [taken]);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(1L);
    }

    [Test]
    public async Task Block_LastAssignment_GetValueNotHandleUnwrap() {
        var x = new Variable("x");
        var node = new Block([
            new Constant("alloc-a"),
            new Constant("alloc-b"),
            new Assignment(x, new Constant(7L))
        ], [x]);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.Result.Kind).IsEqualTo(InterpreterResult.ResultKind.Value);
        await Assert.That(exec.GetValue<long>()).IsEqualTo(7L);
    }

    [Test]
    public async Task Block_LastIfStatement_IsVoid() {
        var x = new Variable("x");
        var node = new Block([
            new Assignment(x, new Constant(1L)),
            new IfStatement(new Constant(true), new Constant(2L))
        ], [x]);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.Result.IsVoid).IsTrue();
    }

    [Test]
    public async Task Comment_OnlyInBlock_IsVoid() {
        using var exec = Interpreter.Execute(Interpreter.Compile(new Block([new Comment("note")])));
        await Assert.That(exec.Result.IsVoid).IsTrue();
    }

    [Test]
    public async Task Coalesce_EmptyString_KeepsLeft() {
        var node = new Coalesce(new Constant(""), new Constant("fallback"));
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<string>()).IsEqualTo("");
    }

    [Test]
    public async Task Coalesce_NonNullableZero_KeepsZero() {
        var node = new Coalesce(new Constant(0), new Constant(99));
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(0L);
    }

    [Test]
    public async Task Coalesce_LongZero_KeepsZero() {
        var node = new Coalesce(new Constant(0L), new Constant(99L));
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(0L);
    }

    [Test]
    public async Task TypeCast_PrimitiveTypeReference_Int32() {
        var node = new TypeCast(new Constant(42L), new PrimitiveTypeReference(PrimitiveType.Int32));
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<int>()).IsEqualTo(42);
    }

    [Test]
    public async Task NestedLambda_InnerParamDifferentName_CallsThrough() {
        var innerP = new Parameter("y", TypeReference.To<long>());
        var inner = new Lambda([innerP], new Add(innerP, new Constant(1L)));
        var outerP = new Parameter("x", TypeReference.To<long>());
        var outer = new Lambda([outerP], new Invoke(inner, outerP));
        var node = new Invoke(outer, new Constant(41L));
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(42L);
    }

    [Test]
    public async Task Subtract_IntAndDouble_Promotes() {
        var node = new Subtract(new Constant(10), new Constant(2.5));
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<double>()).IsEqualTo(7.5);
    }

    [Test]
    public async Task Return_Value_ExitsBlock() {
        var node = new Block([
            new Return(new Constant(7L)),
            new Constant(99L)
        ]);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(7L);
    }

    [Test]
    public async Task Lambda_SameNameDifferentParameterInstance_BindsAsOwn() {
        var own = new Parameter("x", TypeReference.To<long>());
        var other = new Parameter("x", TypeReference.To<long>());
        var node = new Invoke(new Lambda([own], other), new Constant(7L));
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(7L);
    }

    [Test]
    public async Task StoredLambda_SameNameDifferentParameterInstance_BindsAsOwn() {
        var fn = new Variable("fn");
        var own = new Parameter("x", TypeReference.To<long>());
        var other = new Parameter("x", TypeReference.To<long>());
        var node = new Block([
            new Assignment(fn, new Lambda([own], other)),
            new Invoke(fn, new Constant(7L))
        ], [fn]);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.RawValue).IsEqualTo(7L);
    }

    [Test]
    public async Task ULong_MaxValue_RoundTripsAsBits() {
        var node = new Constant(ulong.MaxValue);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.RawValue).IsEqualTo(unchecked((long)ulong.MaxValue));
        await Assert.That(exec.GetValue<ulong>()).IsEqualTo(ulong.MaxValue);
    }

    [Test]
    public async Task ULong_SmallValue_IsUnsigned() {
        var node = new Constant(150UL);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<ulong>()).IsEqualTo(150UL);
    }

    [Test]
    public async Task SetArgs_ULongMaxValue_RoundTrips() {
        var p = new Parameter("x", TypeReference.To<ulong>());
        var program = Interpreter.Compile(p);
        using var exec = Interpreter.Execute(program, s => s.SetArgs(ulong.MaxValue));
        await Assert.That(exec.GetValue<ulong>()).IsEqualTo(ulong.MaxValue);
    }

    [Test]
    public async Task StoredClosure_MutateAfterStore_SeesLatestValue() {
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
    public async Task StoredClosure_Write_IsVisibleToOuter() {
        var captured = new Variable("captured");
        var fn = new Variable("fn");
        var node = new Block([
            new Assignment(captured, new Constant(1L)),
            new Assignment(fn, new Lambda([], new Assignment(captured, new Constant(2L)))),
            new Invoke(fn),
            captured
        ], [captured, fn]);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.RawValue).IsEqualTo(2L);
    }

    [Test]
    public async Task TwoStoredClosures_ShareUpvalue() {
        var captured = new Variable("captured");
        var reader = new Variable("reader");
        var writer = new Variable("writer");
        var node = new Block([
            new Assignment(captured, new Constant(1L)),
            new Assignment(reader, new Lambda([], captured)),
            new Assignment(writer, new Lambda([], new Assignment(captured, new Constant(3L)))),
            new Invoke(writer),
            new Invoke(reader)
        ], [captured, reader, writer]);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.RawValue).IsEqualTo(3L);
    }

    [Test]
    public async Task NestedStoredClosure_SharesOuterUpvalue() {
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
        await Assert.That(exec.RawValue).IsEqualTo(4L);
    }
}