using Poly.Interpretation;
using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Tests.Interpretation;

/// <summary>Language-VM oracles: Interpreter.Compile + execute (or compile-reject) per executable CompileNodeInner kind, no DomainModeling.</summary>
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
    public async Task Assignment_DoubleToMetersMember_UsesImplicitOperator() {
        var host = new Variable("host");
        var assignLength = new Assignment(new Member(host, "Length"), new Constant(2.5));
        var node = new Block([
            new Assignment(host, new New(TypeReference.To<Poly.Tests.Introspection.TypeCompatibilityTests.ConversionHost>())),
            assignLength,
            new Member(host, "Length")
        ], [host]);
        var analysis = Interpreter.Analyze(node);
        var rewritten = analysis.GetNodeReplacement(assignLength) as Assignment;
        await Assert.That(rewritten).IsNotNull();
        await Assert.That(rewritten!.Value).IsTypeOf<Invoke>();
        using var exec = Interpreter.Execute(Interpreter.Compile(node, analysis));
        await Assert.That(exec.GetValue<Poly.Tests.Introspection.TypeCompatibilityTests.Meters>().Value)
            .IsEqualTo(2.5);
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
    public async Task Add_NestedStrings_FlattensToSingleConcat() {
        var a = new Parameter("a", TypeReference.To<string>());
        var b = new Parameter("b", TypeReference.To<string>());
        var c = new Parameter("c", TypeReference.To<string>());
        var d = new Parameter("d", TypeReference.To<string>());
        var nested = new Add(new Add(a, b), new Add(c, d));
        var analysis = Interpreter.Analyze(nested);
        var rewritten = analysis.GetNodeReplacement(nested) as Invoke;
        await Assert.That(rewritten).IsNotNull();
        await Assert.That(rewritten!.Arguments.Length).IsEqualTo(4);
        var program = Interpreter.Compile(nested, analysis);
        using var exec = Interpreter.Execute(program, s => s.SetArgs("a", "b", "c", "d"));
        await Assert.That(exec.GetValue<string>()).IsEqualTo("abcd");
    }

    [Test]
    public async Task Add_FiveStrings_FlattensToEnumerableConcat() {
        var w = new Parameter("w", TypeReference.To<string>());
        var x = new Parameter("x", TypeReference.To<string>());
        var y = new Parameter("y", TypeReference.To<string>());
        var y2 = new Parameter("y2", TypeReference.To<string>());
        var z = new Parameter("z", TypeReference.To<string>());
        var node = new Add(new Add(new Add(w, x), y), new Add(y2, z));
        var analysis = Interpreter.Analyze(node);
        var rewritten = analysis.GetNodeReplacement(node) as Invoke;
        await Assert.That(rewritten).IsNotNull();
        await Assert.That(rewritten!.Arguments.Length).IsEqualTo(5);
        var program = Interpreter.Compile(node, analysis);
        using var exec = Interpreter.Execute(program, s => s.SetArgs("w", "x", "y", "y", "z"));
        await Assert.That(exec.GetValue<string>()).IsEqualTo("wxyyz");
    }

    [Test]
    public async Task Add_SameParameterRepeated_FlattensAndEvaluates() {
        var name = new Parameter("name", TypeReference.To<string>());
        var sep = new Parameter("sep", TypeReference.To<string>());
        var node = new Add(new Add(name, sep), name);
        var analysis = Interpreter.Analyze(node);
        var rewritten = analysis.GetNodeReplacement(node) as Invoke;
        await Assert.That(rewritten).IsNotNull();
        await Assert.That(rewritten!.Arguments.Length).IsEqualTo(3);
        await Assert.That(ReferenceEquals(rewritten.Arguments[0], rewritten.Arguments[2])).IsTrue();
        var program = Interpreter.Compile(node, analysis);
        using var exec = Interpreter.Execute(program, s => s.SetArgs("Ada", "-"));
        await Assert.That(exec.GetValue<string>()).IsEqualTo("Ada-Ada");
    }

    [Test]
    public async Task Add_DateTimePlusDays_RewritesToAddDays() {
        var start = new DateTime(2026, 1, 1);
        var add = new Add(new Constant(start), new Constant(14));
        var analysis = Interpreter.Analyze(add);
        var rewritten = analysis.GetNodeReplacement(add);
        await Assert.That(rewritten).IsTypeOf<Invoke>();
        using var exec = Interpreter.Execute(Interpreter.Compile(add, analysis));
        await Assert.That(exec.GetValue<DateTime>()).IsEqualTo(new DateTime(2026, 1, 15));
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

    [Test]
    public async Task Multiply_Longs() {
        using var exec = Interpreter.Execute(Interpreter.Compile(new Multiply(new Constant(6L), new Constant(7L))));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(42L);
    }

    [Test]
    public async Task Divide_Longs() {
        using var exec = Interpreter.Execute(Interpreter.Compile(new Divide(new Constant(20L), new Constant(4L))));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(5L);
    }

    [Test]
    public async Task Modulo_Longs() {
        using var exec = Interpreter.Execute(Interpreter.Compile(new Modulo(new Constant(17L), new Constant(5L))));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(2L);
    }

    [Test]
    public async Task BitwiseOr_Bits() {
        using var exec = Interpreter.Execute(Interpreter.Compile(new BitwiseOr(new Constant(6L), new Constant(3L))));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(7L);
    }

    [Test]
    public async Task BitwiseXor_Bits() {
        using var exec = Interpreter.Execute(Interpreter.Compile(new BitwiseXor(new Constant(6L), new Constant(3L))));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(5L);
    }

    [Test]
    public async Task ShiftLeft_Bits() {
        using var exec = Interpreter.Execute(Interpreter.Compile(new ShiftLeft(new Constant(3L), new Constant(2L))));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(12L);
    }

    [Test]
    public async Task ShiftRight_Bits() {
        using var exec = Interpreter.Execute(Interpreter.Compile(new ShiftRight(new Constant(12L), new Constant(2L))));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(3L);
    }

    [Test]
    public async Task Equal_MatchingLongs_IsTrue() {
        using var exec = Interpreter.Execute(Interpreter.Compile(new Equal(new Constant(3L), new Constant(3L))));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(1L);
    }

    [Test]
    public async Task NotEqual_DifferentLongs_IsTrue() {
        using var exec = Interpreter.Execute(Interpreter.Compile(new NotEqual(new Constant(3L), new Constant(4L))));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(1L);
    }

    [Test]
    public async Task LessThan_OrderedLongs_IsTrue() {
        using var exec = Interpreter.Execute(Interpreter.Compile(new LessThan(new Constant(1L), new Constant(2L))));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(1L);
    }

    [Test]
    public async Task LessThanOrEqual_EqualLongs_IsTrue() {
        using var exec = Interpreter.Execute(Interpreter.Compile(new LessThanOrEqual(new Constant(2L), new Constant(2L))));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(1L);
    }

    [Test]
    public async Task GreaterThan_OrderedLongs_IsTrue() {
        using var exec = Interpreter.Execute(Interpreter.Compile(new GreaterThan(new Constant(5L), new Constant(2L))));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(1L);
    }

    [Test]
    public async Task GreaterThanOrEqual_EqualLongs_IsTrue() {
        using var exec = Interpreter.Execute(Interpreter.Compile(new GreaterThanOrEqual(new Constant(2L), new Constant(2L))));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(1L);
    }

    [Test]
    public async Task Variable_AfterAssign_ReturnsValue() {
        var x = new Variable("x");
        var node = new Block([
            new Assignment(x, new Constant(11L)),
            x
        ], [x]);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(11L);
    }

    [Test]
    public async Task ThisReference_SetArgsSlot0_ReturnsInstance() {
        var instance = new object();
        var program = Interpreter.Compile(new ThisReference());
        using var exec = Interpreter.Execute(program, s => s.SetArgs(instance));
        await Assert.That(exec.GetValue<object>()).IsSameReferenceAs(instance);
    }

    [Test]
    public async Task NamedTypeReference_AsValue_CompileRejected() {
        await Assert.That(() => Interpreter.Compile(new NamedTypeReference("DateTime")))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task TypeReference_AsValue_CompileRejected() {
        await Assert.That(() => Interpreter.Compile(TypeReference.To<string>()))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task PrimitiveTypeReference_AsValue_CompileRejected() {
        await Assert.That(() => Interpreter.Compile(new PrimitiveTypeReference(PrimitiveType.Int32)))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task TypeDefinitionReference_AsValue_CompileRejected() {
        ITypeDefinition stringType = ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(typeof(string));
        await Assert.That(() => Interpreter.Compile(new TypeDefinitionReference(stringType)))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task NullForgiving_YieldsOperand() {
        using var exec = Interpreter.Execute(Interpreter.Compile(new NullForgiving(new Constant(42L))));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(42L);
    }

    [Test]
    public async Task Not_False_IsTrue() {
        using var exec = Interpreter.Execute(Interpreter.Compile(new Not(new Constant(false))));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(1L);
    }

    [Test]
    public async Task UnaryMinus_Long() {
        using var exec = Interpreter.Execute(Interpreter.Compile(new UnaryMinus(new Constant(7L))));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(-7L);
    }

    [Test]
    public async Task BitwiseNot_Long() {
        using var exec = Interpreter.Execute(Interpreter.Compile(new BitwiseNot(new Constant(0L))));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(~0L);
    }

    [Test]
    public async Task Conditional_TrueBranch() {
        var node = new Conditional(new Constant(true), new Constant(1L), new Constant(2L));
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(1L);
    }

    [Test]
    public async Task And_TrueTrue_IsTrue() {
        using var exec = Interpreter.Execute(Interpreter.Compile(new And(new Constant(true), new Constant(true))));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(1L);
    }

    [Test]
    public async Task Or_FalseTrue_IsTrue() {
        using var exec = Interpreter.Execute(Interpreter.Compile(new Or(new Constant(false), new Constant(true))));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(1L);
    }

    [Test]
    public async Task PopCount_SetBits() {
        using var exec = Interpreter.Execute(Interpreter.Compile(new PopCount(new Constant(11L))));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(3L);
    }

    [Test]
    public async Task TypeIs_StringIsString_IsTrue() {
        var node = new TypeIs(new Constant("hello"), TypeReference.To<string>());
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.RawValue).IsEqualTo(1L);
    }

    [Test]
    public async Task Member_StringLength() {
        var node = new Member(new Constant("hi"), "Length");
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<int>()).IsEqualTo(2);
    }

    [Test]
    public async Task WhileLoop_CountsToThree() {
        var i = new Variable("i");
        var node = new Block([
            new Assignment(i, new Constant(0L)),
            new WhileLoop(new LessThan(i, new Constant(3L)), new Assignment(i, new Add(i, new Constant(1L)))),
            i
        ], [i]);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(3L);
    }

    [Test]
    public async Task DoWhileLoop_RunsAtLeastOnce() {
        var i = new Variable("i");
        var node = new Block([
            new Assignment(i, new Constant(0L)),
            new DoWhileLoop(new Assignment(i, new Add(i, new Constant(1L))), new LessThan(i, new Constant(3L))),
            i
        ], [i]);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(3L);
    }

    [Test]
    public async Task ForLoop_CountsToFive() {
        var i = new Variable("i");
        var node = new Block([
            new ForLoop(
                new Assignment(i, new Constant(0L)),
                new LessThan(i, new Constant(5L)),
                new Assignment(i, new Add(i, new Constant(1L))),
                new Constant(0L)),
            i
        ], [i]);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(5L);
    }

    [Test]
    public async Task ForEachLoop_SumsArray() {
        var sum = new Variable("sum");
        var item = new Variable("item");
        var node = new Block([
            new Assignment(sum, new Constant(0L)),
            new ForEachLoop(item, new Constant(new long[] { 1L, 2L, 3L }), new Assignment(sum, new Add(sum, item))),
            sum
        ], [sum]);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(6L);
    }

    [Test]
    public async Task BreakStatement_ExitsWhile() {
        var i = new Variable("i");
        var node = new Block([
            new Assignment(i, new Constant(0L)),
            new WhileLoop(
                new Constant(true),
                new Block([
                    new IfStatement(new Equal(i, new Constant(3L)), new BreakStatement()),
                    new Assignment(i, new Add(i, new Constant(1L)))
                ])),
            i
        ], [i]);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(3L);
    }

    [Test]
    public async Task ContinueStatement_SkipsWhileBody() {
        var i = new Variable("i");
        var seen = new Variable("seen");
        var node = new Block([
            new Assignment(i, new Constant(0L)),
            new Assignment(seen, new Constant(0L)),
            new WhileLoop(
                new LessThan(i, new Constant(3L)),
                new Block([
                    new Assignment(i, new Add(i, new Constant(1L))),
                    new IfStatement(new Equal(i, new Constant(2L)), new ContinueStatement()),
                    new Assignment(seen, new Add(seen, new Constant(1L)))
                ])),
            seen
        ], [i, seen]);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(2L);
    }

    [Test]
    public async Task Goto_AndLabel_SkipAssignment() {
        var x = new Variable("x");
        var node = new Block([
            new Assignment(x, new Constant(10L)),
            new GotoStatement("exit"),
            new Assignment(x, new Constant(20L)),
            new LabelDeclaration("exit", x)
        ], [x]);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(10L);
    }

    [Test]
    public async Task ThrowStatement_ThrowsOperand() {
        var node = new ThrowStatement(new New(TypeReference.To<InvalidOperationException>(), new Constant("vm")));
        await Assert.That(() => {
            using var exec = Interpreter.Execute(Interpreter.Compile(node));
        }).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task TryCatchFinally_CatchSetsFlag() {
        var caught = new Variable("caught");
        var node = new Block([
            new Assignment(caught, new Constant(0L)),
            new TryCatchFinally(
                new ThrowStatement(new New(TypeReference.To<InvalidOperationException>())),
                CatchClauses: [
                    new CatchClause(TypeReference.To<InvalidOperationException>(), "ex", new Assignment(caught, new Constant(1L)))
                ]),
            caught
        ], [caught]);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(1L);
    }

    [Test]
    public async Task UsingStatement_DisposesResource() {
        var resource = new TrackingDisposable();
        var node = new UsingStatement(new Constant(resource), new Constant(1L));
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(resource.Disposed).IsTrue();
    }

    [Test]
    public async Task SuspendNode_Suspends() {
        var program = Interpreter.Compile(new SuspendNode(new Constant(5L), "bp"));
        using var exec = Interpreter.Execute(program);
        await Assert.That(exec.IsSuspended).IsTrue();
    }

    [Test]
    public async Task StridedSetBits_SetsWordBits() {
        var arr = new Variable("arr");
        var node = new Block([
            new Assignment(arr, new NewArray(TypeReference.To<long>(), new Constant(2L))),
            new StridedSetBits(arr, new Constant(4L), new Constant(2L), new Constant(8L)),
            new IndexAccess(arr, new Constant(0L))
        ], [arr]);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(336L);
    }

    [Test]
    public async Task IfStatement_ThenBranch_Runs() {
        var x = new Variable("x");
        var node = new Block([
            new Assignment(x, new Constant(0L)),
            new IfStatement(new Constant(true), new Assignment(x, new Constant(7L)), new Assignment(x, new Constant(9L))),
            x
        ], [x]);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(7L);
    }

    [Test]
    public async Task CompilationUnit_AsProgram_CompileRejected() {
        await Assert.That(() => Interpreter.Compile(new CompilationUnitNode([], null, [], null)))
            .Throws<Exception>();
    }

    [Test]
    public async Task TypeDefinitionNode_AsProgram_NotExecutable() {
        var node = new TypeDefinitionNode("Widget", "Sample");
        await Assert.That(() => Interpreter.Compile(node)).Throws<Exception>();
    }

    private sealed class TrackingDisposable : IDisposable {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }
}
