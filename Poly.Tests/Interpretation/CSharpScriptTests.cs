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
}