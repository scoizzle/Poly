using Poly.Interpretation.CSharp;

namespace Poly.Tests.Interpretation;

/// <summary>
/// Oracles for <see cref="CSharpScript"/> / <see cref="ScriptDebugSession"/>:
/// read a C#-subset snippet, run it on the VM, and step it, including the
/// lifecycle hazards (runaway loops, dispose during an in-flight step).
/// </summary>
public class CSharpScriptTests {
    [Test]
    public async Task Run_Arithmetic_ReturnsFoldedValue() {
        var result = CSharpScript.Run("2 + 3 * 4");
        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Value).IsEqualTo("14");
    }

    [Test]
    public async Task Run_VarDeclaration_ThenExpression_UsesLocal() {
        var result = CSharpScript.Run("var x = 2 + 3;\nx * 2");
        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Value).IsEqualTo("10");
    }

    [Test]
    public async Task Run_WhileAccumulator_ReturnsSum() {
        var result = CSharpScript.Run(
            "var i = 0;\nvar sum = 0;\nwhile (i < 5) { sum = sum + i; i = i + 1; }\nsum");
        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Value).IsEqualTo("10");
    }

    [Test]
    public async Task Run_TypedLocal_DoubleDividesAsDouble() {
        var result = CSharpScript.Run("double x = 1;\nx / 2");
        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Value).IsEqualTo("0.5");
    }

    [Test]
    public async Task Run_TypedLocal_DoubleSurvivesReassignment() {
        var result = CSharpScript.Run("double x = 1;\nx = 2;\nx / 2");
        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Value).IsEqualTo("1");
    }

    [Test]
    public async Task Run_TypedLocal_NarrowingReassignment_FailsClosed() {
        // C# rejects `int x = 1; x = 2.5;` (no implicit double -> int), and a silent
        // truncation would be worse than an error.
        var result = CSharpScript.Run("int x = 1;\nx = 2.5;\nx");
        await Assert.That(result.Success).IsFalse();
    }

    [Test]
    public async Task Run_TypedLocal_DecimalWrite_FailsClosed() {
        // A decimal slot is a heap handle; a raw scalar cannot be converted into it.
        var result = CSharpScript.Run("decimal d = 1;\nd = 2;\nd");
        await Assert.That(result.Success).IsFalse();
    }

    [Test]
    public async Task Run_TypedLocal_DoubleReassignedToDouble() {
        var result = CSharpScript.Run("double x = 1;\nx = 2.5;\nx * 2");
        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Value).IsEqualTo("5");
    }

    [Test]
    public async Task Run_ConstLocal_ThenExpression_UsesLocal() {
        var result = CSharpScript.Run("const int k = 3;\nk + 1");
        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Value).IsEqualTo("4");
    }

    [Test]
    public async Task ReadProgram_ConstLocal_FoldsToTypedDeclaration() {
        var block = (Block)CSharpScript.ReadProgram("const int k = 3;\nk + 1");
        var assignment = (Assignment)block.Nodes[0];
        await Assert.That(assignment.Destination).IsTypeOf<Variable>();
        await Assert.That(((Variable)assignment.Destination).Name).IsEqualTo("k");
        await Assert.That(assignment.Value).IsTypeOf<TypeCast>();
        await Assert.That(block.Variables).Count().IsEqualTo(1);
    }

    [Test]
    public async Task Read_ConstLocal_RoundTripsAsVarDeclaration() {
        // A const local folds to the same typed declaration a `var`/typed local
        // produces, so the writer normalizes it to `var`; the second pass is stable.
        const string text = "class C { void M() { const int k = 3; } }";
        var written = CSharpLanguage.Write(CSharpLanguage.Read(text));
        var second = CSharpLanguage.Write(CSharpLanguage.Read(written));
        await Assert.That(second).IsEqualTo(written);
        await Assert.That(written).Contains("var k = (int)3;");
    }

    [Test]
    public async Task Run_ConstLocal_ImplicitType_FailsClosed() {
        // C# requires an explicit type on a const local; `const var` must not fold.
        var result = CSharpScript.Run("const var k = 3;\nk");
        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.Diagnostics).IsNotEmpty();
    }

    [Test]
    public async Task Run_ConstLocal_WithoutInitializer_FailsClosed() {
        // A const must be initialized, so the bare declaration is not a valid form.
        var result = CSharpScript.Run("const int k;\nk");
        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.Diagnostics).IsNotEmpty();
    }

    [Test]
    public async Task Run_ConditionalAccess_LiteralReceiver() {
        var value = CSharpScript.Run("\"hi\"?.Length");
        await Assert.That(value.Success).IsTrue();
        await Assert.That(value.Value).IsEqualTo("2");

        var compared = CSharpScript.Run("\"hi\"?.Length == 2");
        await Assert.That(compared.Success).IsTrue();
        await Assert.That(compared.Value).IsEqualTo("true");
    }

    [Test]
    public async Task Run_LocalMemberAccess_Resolves() {
        var result = CSharpScript.Run("var s = \"hi\";\ns.Length");
        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Value).IsEqualTo("2");
    }

    [Test]
    public async Task Run_ConditionalAccess_LocalReceiver() {
        var result = CSharpScript.Run("var s = \"hi\";\ns?.Length");
        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Value).IsEqualTo("2");
    }

    [Test]
    public async Task Run_ConditionalAccess_LocalReceiverArithmetic() {
        var result = CSharpScript.Run("var s = \"hi\";\ns?.Length + 1");
        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Value).IsEqualTo("3");
    }

    [Test]
    public async Task Run_ConditionalAccess_LocalReceiverCompare() {
        var result = CSharpScript.Run("var s = \"hi\";\ns?.Length == 2");
        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Value).IsEqualTo("true");
    }

    [Test]
    public async Task Run_ConditionalAccess_NullReceiverValueMember_FailsClosed() {
        // C# types `s?.Length` as `int?`; this subset has no nullable value type, and
        // the null arm has no slot representation. Yielding the sentinel 0 would be a
        // silently wrong value, so the form fails closed when the receiver is null.
        var result = CSharpScript.Run("string s = null;\ns?.Length");
        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.Diagnostics.Any(d => d.Contains("Conditional access"))).IsTrue();
    }

    [Test]
    public async Task Run_ConditionalAccess_NullReceiverValueMemberArithmetic_FailsClosed() {
        var result = CSharpScript.Run("string s = null;\ns?.Length + 1");
        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.Diagnostics.Any(d => d.Contains("Conditional access"))).IsTrue();
    }

    [Test]
    public async Task Run_ConditionalAccess_NonNullReceiverValueMember_StillReads() {
        // The null arm is the fail-closed one; a non-null receiver must keep working.
        var result = CSharpScript.Run("string s = \"hi\";\ns?.Length + 1");
        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Value).IsEqualTo("3");
    }

    [Test]
    public async Task Run_ForeachLoopVariable_MemberAccessResolves() {
        var result = CSharpScript.Run(
            "var xs = new string[2];\nxs[0] = \"hi\";\nxs[1] = \"yo\";\nvar total = 0;\nforeach (var x in xs) { total = total + x.Length; }\ntotal");
        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Value).IsEqualTo("4");
    }

    [Test]
    public async Task Run_TypedLocal_IntDividesAsInt() {
        var result = CSharpScript.Run("int x = 7;\nx / 2");
        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Value).IsEqualTo("3");
    }

    [Test]
    public async Task Run_TypedForeach_IteratesArray() {
        var result = CSharpScript.Run(
            "var xs = new long[3];\nvar total = 0;\nforeach (long n in xs) { total = total + 1; }\ntotal");
        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Value).IsEqualTo("3");
    }

    private static async Task AssertValue(string snippet, string expected) {
        var result = CSharpScript.Run(snippet);
        await Assert.That(result.Success).IsTrue().Because(string.Join(" | ", result.Diagnostics));
        await Assert.That(result.Value).IsEqualTo(expected);
    }

    [Test]
    public async Task Run_CompoundAssignment_AllSingleTokenOperators() {
        await AssertValue("var x = 10;\nx -= 3;\nx", "7");
        await AssertValue("var x = 10;\nx *= 3;\nx", "30");
        await AssertValue("var x = 9;\nx /= 2;\nx", "4");
        await AssertValue("var x = 9;\nx %= 4;\nx", "1");
        await AssertValue("var x = 6;\nx |= 1;\nx", "7");
        await AssertValue("var x = 6;\nx ^= 3;\nx", "5");
    }

    [Test]
    public async Task Run_TypedLocal_OtherDeclaredTypes() {
        await AssertValue("long a = 2;\na * 3", "6");
        await AssertValue("bool b = true;\nb", "true");
        await AssertValue("string s = \"x\";\ns.Length", "1");
        await AssertValue("float f = 4;\nf / 2", "2");
    }

    [Test]
    public async Task Run_LambdaParameter_UsedInBody() {
        await AssertValue("((long a) => a + 1)(4)", "5");
        await AssertValue("((string s) => s.Length)(\"hi\")", "2");
    }

    [Test]
    public async Task Run_LambdaParameter_ShadowsOuterLocal() {
        // The lambda parameter must shadow the outer local of the same name, for both
        // the parameter's use (5, not the outer string) and the captured outer read.
        await AssertValue("var s = \"hi\";\n((long s) => s + 1)(4)", "5");
        await AssertValue("var s = \"hi\";\n((string t) => s.Length + t.Length)(\"yo\")", "4");
    }

    [Test]
    public async Task Run_FrameInvoke_DoesNotClobberCallerSlots() {
        // The 0-arg frame path used to write its result into ring slot 0, discarding a
        // caller value that was already there (an earlier interpolation hole, or an
        // inline parameter argument).
        await AssertValue("((long a) => { var t = ((long b) => b)(); a; })(4)", "4");
        await AssertValue("var x = 1;\n$\"{x}{((long b) => 2)()}\"", "\"12\"");
    }

    [Test]
    public async Task Run_InterpolatedString_Executes() {
        await AssertValue("$\"x = {1 + 1}\"", "\"x = 2\"");
        await AssertValue("var y = 5;\n$\"y = {y}\"", "\"y = 5\"");
        await AssertValue("var s = $\"hi\";\ns.Length", "2");
        await AssertValue("$\"{{lit}}\"", "\"{lit}\"");
    }

    [Test]
    public async Task Run_InterpolatedString_FormatSpecifier_FailsClosed() {
        var result = CSharpScript.Run("var x = 1;\n$\"{x:N2}\"");
        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.Diagnostics).IsNotEmpty();
    }

    [Test]
    public async Task Run_LocalShadowingLambdaParameter_FailsClosed() {
        // C# forbids a local that shadows a lambda parameter (CS0136), and the emitter
        // resolves parameter uses by name — so this must fail closed, not mis-bind.
        var result = CSharpScript.Run("((long a) => { var a = 9; a; })(4)");
        await Assert.That(result.Success).IsFalse();
    }

    [Test]
    public async Task Read_InterpolatedString_CharBraceHole_RoundTrips() {
        // A hole whose printed form contains a raw '}' (a char literal) must still
        // round-trip: the reader skips char literals when finding the hole's end.
        const string text = "class C { void M() { $\"{'}'}\"; } }";
        var first = CSharpLanguage.Read(text);
        var written = CSharpLanguage.Write(first);
        var second = CSharpLanguage.Write(CSharpLanguage.Read(written));
        await Assert.That(second).IsEqualTo(written);
    }

    [Test]
    public async Task Run_CompoundAssignment_UpdatesVariable() {
        var result = CSharpScript.Run("var x = 1;\nx += 2;\nx");
        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Value).IsEqualTo("3");
    }

    [Test]
    public async Task Run_CompoundBitwise_UpdatesVariable() {
        var result = CSharpScript.Run("var flags = 6;\nflags &= 3;\nflags");
        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Value).IsEqualTo("2");
    }

    [Test]
    public async Task Run_ConcatOfLocalAndLiteral_AppendsString() {
        var result = CSharpScript.Run("var s = \"a\";\ns + \"b\"");
        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Value).IsEqualTo("\"ab\"");
    }

    [Test]
    public async Task Run_CompoundConcatOnLocal_AppendsString() {
        var result = CSharpScript.Run("var s = \"a\";\ns += \"b\";\ns");
        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Value).IsEqualTo("\"ab\"");
    }

    [Test]
    public async Task Run_Comparison_ReturnsBoolean() {
        var result = CSharpScript.Run("1 < 2");
        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Value).IsEqualTo("true");
        await Assert.That(result.ValueType).IsEqualTo("Boolean");
    }

    [Test]
    public async Task Run_StringLiteral_ReturnsQuotedValue() {
        var result = CSharpScript.Run("\"hello\"");
        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Value).IsEqualTo("\"hello\"");
        await Assert.That(result.ValueType).IsEqualTo("String");
    }

    [Test]
    public async Task Run_EmptySnippet_ReportsDiagnostic() {
        var result = CSharpScript.Run("   ");
        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.Diagnostics).IsNotEmpty();
    }

    [Test]
    public async Task Run_InvalidSyntax_ReportsDiagnostic() {
        var result = CSharpScript.Run("var = 1");
        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.Diagnostics).IsNotEmpty();
    }

    [Test]
    public async Task Run_RunawayLoop_StopsAtIterationCap() {
        var result = CSharpScript.Run("var i = 0;\nwhile (true) { i = i + 1; }", maxLoopIterations: 1_000);
        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.Diagnostics.Any(diagnostic => diagnostic.Contains("MaxLoopIterations"))).IsTrue();
    }

    [Test]
    public async Task ReadProgram_Empty_Throws() {
        await Assert.That(() => CSharpScript.ReadProgram("")).Throws<FormatException>();
    }

    [Test]
    public async Task Debug_Start_PausesThenStepsAndCompletes() {
        var session = ScriptDebugSession.TryStart("var x = 2;\nvar y = 3;\nx + y", out var diagnostics);
        await Assert.That(diagnostics).IsEmpty();
        await Assert.That(session).IsNotNull();
        using var debug = session!;

        await Assert.That(debug.CurrentNodeText).IsNotEmpty();

        await debug.StepAsync();
        await Assert.That(debug.CurrentNodeText).IsNotEmpty();

        await debug.ContinueAsync();
        await Assert.That(debug.IsCompleted).IsTrue();
    }

    [Test]
    public async Task Debug_Step_ExposesNamedLocal() {
        var session = ScriptDebugSession.TryStart(
            "var x = 0;\nvar i = 0;\nwhile (i < 3) { x = x + 1; i = i + 1; }\nx", out var diagnostics);
        await Assert.That(diagnostics).IsEmpty();
        using var debug = session!;

        // Step into the loop; the block's named locals are visible mid-run.
        await debug.StepAsync();
        await debug.StepAsync();
        await debug.StepAsync();
        await Assert.That(debug.Locals.Any(local => local.Name == "x")).IsTrue();
    }

    [Test]
    public async Task Debug_Continue_RunawayLoop_FaultsAtIterationCap() {
        var session = ScriptDebugSession.TryStart(
            "var i = 0;\nwhile (true) { i = i + 1; }", out var diagnostics, maxLoopIterations: 1_000);
        await Assert.That(diagnostics).IsEmpty();
        await Assert.That(session).IsNotNull();
        using var debug = session!;

        // WaitAsync guards the suite against a regression that drops the cap.
        await debug.ContinueAsync().WaitAsync(TimeSpan.FromSeconds(30));
        await Assert.That(debug.IsCompleted).IsTrue();
        await Assert.That(debug.Fault).IsNotNull();
    }

    [Test]
    public async Task Debug_DisposeDuringInFlightContinue_DoesNotThrow() {
        var session = ScriptDebugSession.TryStart(
            "var i = 0;\nwhile (i < 100000000) { i = i + 1; }", out _, maxLoopIterations: 1_000_000_000);
        await Assert.That(session).IsNotNull();
        var debug = session!;

        var pending = debug.ContinueAsync();
        debug.Dispose();
        try {
            await pending;
        }
        catch {
            // Cancelled by dispose: expected.
        }
        debug.Dispose(); // Idempotent.

        await Assert.That(debug.IsBusy).IsFalse();
    }

    // `is` type tests. The reader folds the target to a primitive/named reference, which
    // the emitter must resolve (it used to test every operand against bool, so all were
    // false). A scalar is answered from its static type; a heap operand at runtime.
    [Test]
    public async Task Run_Is_StringLiteralIsString_IsTrue() {
        var r = CSharpScript.Run("\"hi\" is string");
        await Assert.That(r.Success).IsTrue();
        await Assert.That(r.Value).IsEqualTo("true");
    }

    [Test]
    public async Task Run_Is_IntLiteralIsInt_IsTrue() {
        var r = CSharpScript.Run("1 is int");
        await Assert.That(r.Success).IsTrue();
        await Assert.That(r.Value).IsEqualTo("true");
    }

    [Test]
    public async Task Run_Is_IntLiteralIsLong_IsFalse() {
        // C# `is` tests the runtime type and never widens: a boxed Int32 is not an Int64.
        var r = CSharpScript.Run("1 is long");
        await Assert.That(r.Success).IsTrue();
        await Assert.That(r.Value).IsEqualTo("false");
    }

    [Test]
    public async Task Run_Is_LongLiteralIsLong_IsTrue() {
        var r = CSharpScript.Run("1L is long");
        await Assert.That(r.Success).IsTrue();
        await Assert.That(r.Value).IsEqualTo("true");
    }

    [Test]
    public async Task Run_Is_ScalarIsObject_IsTrue() {
        var r = CSharpScript.Run("1 is object");
        await Assert.That(r.Success).IsTrue();
        await Assert.That(r.Value).IsEqualTo("true");
    }

    [Test]
    public async Task Run_Is_LocalStringIsString_IsTrue() {
        var r = CSharpScript.Run("var o = \"hi\";\no is string");
        await Assert.That(r.Success).IsTrue();
        await Assert.That(r.Value).IsEqualTo("true");
    }

    [Test]
    public async Task Run_Is_LocalStringIsObject_IsTrue() {
        var r = CSharpScript.Run("var o = \"hi\";\no is object");
        await Assert.That(r.Success).IsTrue();
        await Assert.That(r.Value).IsEqualTo("true");
    }

    [Test]
    public async Task Run_Is_NullReferenceIsString_IsFalse() {
        // A null reference is not an instance of anything — the runtime handle is 0.
        var r = CSharpScript.Run("string s = null;\ns is string");
        await Assert.That(r.Success).IsTrue();
        await Assert.That(r.Value).IsEqualTo("false");
    }

    [Test]
    public async Task Run_Is_StringIsInt_IsFalse() {
        var r = CSharpScript.Run("\"hi\" is int");
        await Assert.That(r.Success).IsTrue();
        await Assert.That(r.Value).IsEqualTo("false");
    }

    [Test]
    public async Task Run_Is_UnresolvableTarget_FailsClosed() {
        // An array type reference has no CLR mapping; the emitter must throw, not answer false.
        var r = CSharpScript.Run("var x = \"hi\";\nx is int[]");
        await Assert.That(r.Success).IsFalse();
        await Assert.That(r.Diagnostics.Any(d => d.Contains("cannot resolve the type"))).IsTrue();
    }

    // A declared foreach element type is honored: the element is converted per item. The
    // discriminating value cases live in ForEachDeclaredTypeVmTests (the console surface
    // cannot populate an array yet — array index reads/writes are broken).
    [Test]
    public async Task Run_ForeachDeclaredDouble_OverLongArray_Executes() {
        var r = CSharpScript.Run(
            "var a = new long[2];\ndouble t = 0;\nforeach (double d in a) { t = t + d; }\nt");
        await Assert.That(r.Success).IsTrue();
        await Assert.That(r.Value).IsEqualTo("0");
    }

    [Test]
    public async Task Run_ForeachDeclaredInt_OverLongArray_Executes() {
        var r = CSharpScript.Run(
            "var a = new long[2];\nvar t = 0;\nforeach (int d in a) { t = t + d; }\nt");
        await Assert.That(r.Success).IsTrue();
        await Assert.That(r.Value).IsEqualTo("0");
    }

    [Test]
    public async Task Run_ForeachDeclaredType_NotConvertible_FailsClosed() {
        var r = CSharpScript.Run("var a = new long[1];\nforeach (string s in a) { }\n1");
        await Assert.That(r.Success).IsFalse();
        await Assert.That(r.Diagnostics.Any(d => d.Contains("cannot convert each element"))).IsTrue();
    }

    [Test]
    public async Task Run_ForeachDeclaredObject_OverLongArray_FailsClosed() {
        // A value element with a reference loop type needs box-per-item, which the emitter
        // does not do yet, so the form fails closed rather than storing a wrong shape.
        var r = CSharpScript.Run("var a = new long[1];\nforeach (object o in a) { }\n1");
        await Assert.That(r.Success).IsFalse();
        await Assert.That(r.Diagnostics.Any(d => d.Contains("cannot convert each element"))).IsTrue();
    }

    [Test]
    public async Task Run_CharWidensToNumericVariable() {
        var toInt = CSharpScript.Run("int x = 1;\nx = 'a';\nx");
        await Assert.That(toInt.Success).IsTrue();
        await Assert.That(toInt.Value).IsEqualTo("97");

        var toLong = CSharpScript.Run("long x = 1;\nx = 'a';\nx");
        await Assert.That(toLong.Success).IsTrue();
        await Assert.That(toLong.Value).IsEqualTo("97");

        var toDouble = CSharpScript.Run("double x = 1;\nx = 'a';\nx");
        await Assert.That(toDouble.Success).IsTrue();
        await Assert.That(toDouble.Value).IsEqualTo("97");
    }

    [Test]
    public async Task Run_CharArithmetic_IsNumeric() {
        var plusInt = CSharpScript.Run("'a' + 1");
        await Assert.That(plusInt.Success).IsTrue();
        await Assert.That(plusInt.Value).IsEqualTo("98");

        // char + char is int addition in C#, not string concatenation.
        var plusChar = CSharpScript.Run("'a' + 'b'");
        await Assert.That(plusChar.Success).IsTrue();
        await Assert.That(plusChar.Value).IsEqualTo("195");

        var plusDouble = CSharpScript.Run("'a' + 1.5");
        await Assert.That(plusDouble.Success).IsTrue();
        await Assert.That(plusDouble.Value).IsEqualTo("98.5");
    }

    [Test]
    public async Task Run_CharComparedToInt_IsNumeric() {
        var r = CSharpScript.Run("'a' > 96");
        await Assert.That(r.Success).IsTrue();
        await Assert.That(r.Value).IsEqualTo("true");
    }

    [Test]
    public async Task Run_CharToIntCast_IsNumeric() {
        var r = CSharpScript.Run("(int)'a'");
        await Assert.That(r.Success).IsTrue();
        await Assert.That(r.Value).IsEqualTo("97");
    }

    [Test]
    public async Task Run_IntToCharAssignment_FailsClosed() {
        // C# requires an explicit cast; an implicit int -> char assignment stays rejected.
        var r = CSharpScript.Run("char c = 'a';\nc = 98;\nc");
        await Assert.That(r.Success).IsFalse();
        await Assert.That(r.Diagnostics.Any(d => d.Contains("incompatible"))).IsTrue();
    }

    [Test]
    public async Task Run_StringConcat_RendersNonStringLiteralParts() {
        var intPart = CSharpScript.Run("\"a\" + 1");
        await Assert.That(intPart.Success).IsTrue();
        await Assert.That(intPart.Value).IsEqualTo("\"a1\"");

        var charPart = CSharpScript.Run("'a' + \"b\"");
        await Assert.That(charPart.Success).IsTrue();
        await Assert.That(charPart.Value).IsEqualTo("\"ab\"");

        var doublePart = CSharpScript.Run("\"x\" + 1.5");
        await Assert.That(doublePart.Success).IsTrue();
        await Assert.That(doublePart.Value).IsEqualTo("\"x1.5\"");

        var boolPart = CSharpScript.Run("\"x\" + true");
        await Assert.That(boolPart.Success).IsTrue();
        await Assert.That(boolPart.Value).IsEqualTo("\"xTrue\"");

        var leftInt = CSharpScript.Run("1 + \"b\"");
        await Assert.That(leftInt.Success).IsTrue();
        await Assert.That(leftInt.Value).IsEqualTo("\"1b\"");
    }

    [Test]
    public async Task Run_StringConcat_NonStringVariable_FailsClosed() {
        // A non-literal non-string part would need boxing into a Concat(object) argument,
        // which the emitter does not do; it fails closed rather than emitting garbage.
        var r = CSharpScript.Run("\"a\" + 1 + \"b\"");
        await Assert.That(r.Success).IsTrue();
        await Assert.That(r.Value).IsEqualTo("\"a1b\"");

        var nonLiteral = CSharpScript.Run("var x = 1;\n\"a\" + x");
        await Assert.That(nonLiteral.Success).IsFalse();
        await Assert.That(nonLiteral.Diagnostics.Any(d => d.Contains("string concatenation"))).IsTrue();
    }

    [Test]
    public async Task Run_CharComparedToIntLiteral_FoldsNumeric() {
        var eq = CSharpScript.Run("'a' == 97");
        await Assert.That(eq.Success).IsTrue();
        await Assert.That(eq.Value).IsEqualTo("true");

        var ne = CSharpScript.Run("'a' != 97");
        await Assert.That(ne.Success).IsTrue();
        await Assert.That(ne.Value).IsEqualTo("false");

        var eqDouble = CSharpScript.Run("'a' == 97.0");
        await Assert.That(eqDouble.Success).IsTrue();
        await Assert.That(eqDouble.Value).IsEqualTo("true");
    }

    [Test]
    public async Task Run_CharAssignToNarrowInteger_FailsClosed() {
        // C# rejects the implicit char -> byte/sbyte/short narrowing.
        var r = CSharpScript.Run("byte b = 1;\nb = 'a';\nb");
        await Assert.That(r.Success).IsFalse();
        await Assert.That(r.Diagnostics.Any(d => d.Contains("incompatible"))).IsTrue();

        var wide = CSharpScript.Run("ushort u = 1;\nu = 'a';\nu");
        await Assert.That(wide.Success).IsTrue();
        await Assert.That(wide.Value).IsEqualTo("97");
    }

    [Test]
    public async Task Run_CharArithmetic_PromotesToInt() {
        // `'a' + 1` is an int in C#, so a later int write is allowed.
        var r = CSharpScript.Run("var x = 'a' + 1;\nx = 1000;\nx");
        await Assert.That(r.Success).IsTrue();
        await Assert.That(r.Value).IsEqualTo("1000");
    }

    [Test]
    public async Task Run_StringConcat_LiteralChain_Renders() {
        var r = CSharpScript.Run("\"a\" + 1 + 2");
        await Assert.That(r.Success).IsTrue();
        await Assert.That(r.Value).IsEqualTo("\"a12\"");

        var mixed = CSharpScript.Run("\"a\" + 1 + \"b\" + 2");
        await Assert.That(mixed.Success).IsTrue();
        await Assert.That(mixed.Value).IsEqualTo("\"a1b2\"");

        // A numeric sub-expression part folds to its value first: `'a' + 'b'` is 195 (int).
        var charSum = CSharpScript.Run("'a' + 'b' + \"c\"");
        await Assert.That(charSum.Success).IsTrue();
        await Assert.That(charSum.Value).IsEqualTo("\"195c\"");

        var charPlusInt = CSharpScript.Run("'a' + 1 + \"b\"");
        await Assert.That(charPlusInt.Success).IsTrue();
        await Assert.That(charPlusInt.Value).IsEqualTo("\"98b\"");

        var intSum = CSharpScript.Run("1 + 2 + \"b\"");
        await Assert.That(intSum.Success).IsTrue();
        await Assert.That(intSum.Value).IsEqualTo("\"3b\"");
    }

    [Test]
    public async Task Run_QualifiedTypeName_ResolvesStaticMembers() {
        var nested = CSharpScript.Run("System.String.Empty");
        await Assert.That(nested.Success).IsTrue();
        await Assert.That(nested.Value).IsEqualTo("\"\"");

        var constant = CSharpScript.Run("System.Int32.MaxValue");
        await Assert.That(constant.Success).IsTrue();
        await Assert.That(constant.Value).IsEqualTo("2147483647");

        var call = CSharpScript.Run("System.Math.Abs(-1)");
        await Assert.That(call.Success).IsTrue();
        await Assert.That(call.Value).IsEqualTo("1");
    }

    [Test]
    public async Task Run_QualifiedTypeName_DeepNamespace_Resolves() {
        var r = CSharpScript.Run("System.Threading.Tasks.Task.CompletedTask");
        await Assert.That(r.Success).IsTrue();
        await Assert.That(r.Value).Contains("Task");
    }

    [Test]
    public async Task Run_QualifiedTypeName_InLambdaBody_Resolves() {
        var r = CSharpScript.Run("((long a) => System.Math.Abs(a))(-3)");
        await Assert.That(r.Success).IsTrue();
        await Assert.That(r.Value).IsEqualTo("3");
    }

    [Test]
    public async Task Run_LocalNamedLikeNamespace_ShadowsNamespace() {
        // A declared local named like a namespace root is a value, not a type name.
        var r = CSharpScript.Run("var System = \"xy\";\nSystem.Length");
        await Assert.That(r.Success).IsTrue();
        await Assert.That(r.Value).IsEqualTo("2");
    }

    [Test]
    public async Task Run_QualifiedTypeName_TypeofResolves() {
        var r = CSharpScript.Run("typeof(System.String)");
        await Assert.That(r.Success).IsTrue();
        await Assert.That(r.Value).IsEqualTo("System.String");
    }

    [Test]
    public async Task Run_UnknownDottedName_FailsClosed() {
        var r = CSharpScript.Run("System.Nope.Nothing");
        await Assert.That(r.Success).IsFalse();
        await Assert.That(r.Diagnostics.Any(d => d.Contains("is not declared"))).IsTrue();
    }

    [Test]
    public async Task Run_Nameof_ReturnsTheLastIdentifier() {
        var local = CSharpScript.Run("var x = 1;\nnameof(x)");
        await Assert.That(local.Success).IsTrue();
        await Assert.That(local.Value).IsEqualTo("\"x\"");

        var member = CSharpScript.Run("var s = \"hi\";\nnameof(s.Length)");
        await Assert.That(member.Success).IsTrue();
        await Assert.That(member.Value).IsEqualTo("\"Length\"");

        var qualified = CSharpScript.Run("nameof(System.String)");
        await Assert.That(qualified.Success).IsTrue();
        await Assert.That(qualified.Value).IsEqualTo("\"String\"");
    }

    [Test]
    public async Task Run_Nameof_Concatenates() {
        var r = CSharpScript.Run("var x = 1;\n\"a\" + nameof(x)");
        await Assert.That(r.Success).IsTrue();
        await Assert.That(r.Value).IsEqualTo("\"ax\"");
    }

    [Test]
    public async Task Run_Nameof_InsideLambda_DoesNotCapture() {
        var r = CSharpScript.Run("var x = 1;\n((long a) => nameof(x))(0)");
        await Assert.That(r.Success).IsTrue();
        await Assert.That(r.Value).IsEqualTo("\"x\"");
    }

    [Test]
    public async Task Run_ArrayIndex_ReadWrite() {
        var writeRead = CSharpScript.Run("var a = new long[2];\na[0] = 7;\na[0]");
        await Assert.That(writeRead.Success).IsTrue();
        await Assert.That(writeRead.Value).IsEqualTo("7");

        var unwritten = CSharpScript.Run("var a = new long[2];\na[0]");
        await Assert.That(unwritten.Success).IsTrue();
        await Assert.That(unwritten.Value).IsEqualTo("0");

        var length = CSharpScript.Run("var a = new long[2];\na.Length");
        await Assert.That(length.Success).IsTrue();
        await Assert.That(length.Value).IsEqualTo("2");
    }

    [Test]
    public async Task Run_ArrayIndex_LargeAndReferenceElements() {
        // Above the (removed) small-array threshold, and a reference element read null.
        var large = CSharpScript.Run("var a = new long[5000];\na[0] = 7;\na[0]");
        await Assert.That(large.Success).IsTrue();
        await Assert.That(large.Value).IsEqualTo("7");

        var nullRef = CSharpScript.Run("var a = new string[2];\na[0]");
        await Assert.That(nullRef.Success).IsTrue();
        await Assert.That(nullRef.Value).IsEqualTo("null");

        var refWrite = CSharpScript.Run("var a = new string[3];\na[0] = \"x\";\na[0]");
        await Assert.That(refWrite.Success).IsTrue();
        await Assert.That(refWrite.Value).IsEqualTo("\"x\"");
    }

    [Test]
    public async Task Run_ArrayIndex_ForeachSeesWrittenElement() {
        var r = CSharpScript.Run("var a = new long[2];\na[0] = 7;\nvar t = 0;\nforeach (var x in a) { t = t + x; }\nt");
        await Assert.That(r.Success).IsTrue();
        await Assert.That(r.Value).IsEqualTo("7");
    }

    [Test]
    public async Task Run_ArrayIndex_IeeeElementIsCoerced() {
        // `double[] a; a[0] = 7` must store IEEE bits, as a variable store does.
        var assign = CSharpScript.Run("var a = new double[2];\na[0] = 7;\na[0]");
        await Assert.That(assign.Success).IsTrue();
        await Assert.That(assign.Value).IsEqualTo("7");

        var arithmetic = CSharpScript.Run("var a = new double[2];\na[0] = 7;\na[0] / 2");
        await Assert.That(arithmetic.Success).IsTrue();
        await Assert.That(arithmetic.Value).IsEqualTo("3.5");
    }

    [Test]
    public async Task Run_ArrayIndex_UnsupportedElementType_FailsClosed() {
        // A value-type element that is a heap handle (decimal, DateTime) cannot have its
        // default in a zeroed slot, so the array is rejected rather than read as null.
        var r = CSharpScript.Run("var a = new decimal[2];\na[0]");
        await Assert.That(r.Success).IsFalse();
        await Assert.That(r.Diagnostics.Any(d => d.Contains("is not supported"))).IsTrue();
    }

    [Test]
    public async Task Run_ArrayIndex_FloatingPointIntoIntegerElement_FailsClosed() {
        // C# rejects `long[] a; a[0] = 1.5`; truncating would be a wrong shape.
        var r = CSharpScript.Run("var a = new long[2];\na[0] = 1.5;");
        await Assert.That(r.Success).IsFalse();
        await Assert.That(r.Diagnostics.Any(d => d.Contains("cannot store a floating-point"))).IsTrue();
    }

    [Test]
    public async Task Run_MixedIntegerEquality_IsNumeric() {
        // `1 == 1L`: the runtime keeps both in long slots, so the fold must agree rather
        // than compare a boxed int against a boxed long as unequal.
        var equal = CSharpScript.Run("1 == 1L");
        await Assert.That(equal.Success).IsTrue();
        await Assert.That(equal.Value).IsEqualTo("true");

        var notEqual = CSharpScript.Run("1 != 1L");
        await Assert.That(notEqual.Success).IsTrue();
        await Assert.That(notEqual.Value).IsEqualTo("false");

        // char reaches the same fold path; uint widens against long.
        var chars = CSharpScript.Run("'a' == 'a'");
        await Assert.That(chars.Success).IsTrue();
        await Assert.That(chars.Value).IsEqualTo("true");

        var unsigned = CSharpScript.Run("1U == 1L");
        await Assert.That(unsigned.Success).IsTrue();
        await Assert.That(unsigned.Value).IsEqualTo("true");
    }

    [Test]
    public async Task Run_HexLiteral_ReturnsValue() {
        await AssertValue("0xFF", "255");
        await AssertValue("0x1F + 1", "32");
        await AssertValue("0x10L", "16");
    }

    [Test]
    public async Task Run_HexLiteral_EIsADigitNotAnExponent() {
        // Before the prefix scan, `0xE` folded as `0` followed by the identifier `E`.
        await AssertValue("0xE", "14");
        await AssertValue("0xE + 1", "15");
    }

    [Test]
    public async Task Run_BinaryLiteral_ReturnsValue() {
        await AssertValue("0b1010", "10");
        await AssertValue("0b1010 + 1", "11");
    }

    [Test]
    public async Task Run_DigitSeparators_AreIgnored() {
        await AssertValue("1_000_000", "1000000");
        await AssertValue("0xFF_FF", "65535");
        await AssertValue("0b1010_1010", "170");
        await AssertValue("1_0.5_0", "10.5");
        await AssertValue("1e0_1", "10");
    }

    [Test]
    public async Task Run_HexLiteral_TypedLocalUsesTheValue() {
        await AssertValue("int x = 0xFF;\nx", "255");
        await AssertValue("long y = 0x1F;\ny", "31");
    }

    [Test]
    public async Task Read_HexLiteral_FoldsOneIntConstant() {
        // The accept criterion: `0x1F` must not scan as `0` + identifier `1F`.
        var unit = CSharpLanguage.Read("{ 0x1F; }");
        var block = (Block)unit.TopLevelStatements![0];
        await Assert.That(block.Nodes.Count).IsEqualTo(1);
        var constant = (Constant)block.Nodes[0];
        await Assert.That(constant.Value).IsEqualTo(31);
        // C#'s unsuffixed `0xFF` is an int, which `1 is int`-style folds depend on.
        await Assert.That(constant.Value).IsTypeOf<int>();
    }

    [Test]
    public async Task Read_BinaryLiteral_FoldsOneIntConstant() {
        var unit = CSharpLanguage.Read("{ 0b1010; }");
        var block = (Block)unit.TopLevelStatements![0];
        await Assert.That(block.Nodes.Count).IsEqualTo(1);
        var constant = (Constant)block.Nodes[0];
        await Assert.That(constant.Value).IsEqualTo(10);
        await Assert.That(constant.Value).IsTypeOf<int>();
    }

    [Test]
    public async Task Write_HexLiteral_PrintsDecimalValueAndStaysIdempotent() {
        const string text = "class C { int M() { return 0xFF; } }";
        var written = CSharpLanguage.Write(CSharpLanguage.Read(text));
        await Assert.That(written).Contains("255");
        var again = CSharpLanguage.Write(CSharpLanguage.Read(written));
        await Assert.That(again).IsEqualTo(written);
    }

    [Test]
    public async Task Run_HexLiteralWithoutDigits_FailsClosed() {
        var hex = CSharpScript.Run("0x");
        await Assert.That(hex.Success).IsFalse();
        await Assert.That(hex.Diagnostics.Any(d => d.Contains("Hexadecimal literal"))).IsTrue();
    }

    [Test]
    public async Task Run_BinaryLiteralWithoutDigits_FailsClosed() {
        var binary = CSharpScript.Run("0b");
        await Assert.That(binary.Success).IsFalse();
        await Assert.That(binary.Diagnostics.Any(d => d.Contains("Binary literal"))).IsTrue();
    }
}