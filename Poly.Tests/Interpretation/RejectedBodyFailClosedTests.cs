using Poly.Ast.Nodes;
using Poly.Interpretation;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.CSharp;
using Poly.Interpretation.Vm;
using Poly.Introspection;

namespace Poly.Tests.Interpretation;

/// <summary>
/// A rejected AST method body must not become a name-dispatched host call. Three rules:
/// the reject is carried into the runtime failure so it names its cause; type inference
/// never reflects over an AST type's storage type; and a body that failed on an instance
/// the VM built itself is a compile reject, because no host method can exist there.
/// </summary>
public class RejectedBodyFailClosedTests {
    private static long Run(string source) {
        var unit = CSharpLanguage.Read(source);
        using var exec = Interpreter.Execute(
            Interpreter.Compile(unit.TopLevelStatements![0], Interpreter.Analyze(unit), CompilationMode.NoDebug));
        return exec.RawValue;
    }

    private static Exception CompileOrRun(string source) {
        try {
            Run(source);
        }
        catch (Exception ex) {
            return ex;
        }
        throw new InvalidOperationException("expected the program to fail");
    }

    /// <summary>A VM-constructed receiver is the VM's own dictionary, so there is nothing
    /// to dispatch to. The reject belongs at compile time, naming the real cause — the
    /// old behaviour ran to a <c>Type 'Dictionary`2'</c> failure under a different name.</summary>
    [Test, Timeout(10_000)]
    public async Task RejectedBody_OnVmConstructedInstance_IsACompileReject(CancellationToken ct) {
        _ = ct;
        // A lambda inside an AST method body is a form the frame compiler rejects.
        var ex = CompileOrRun("""
            class P {
                P() { }
                long Run() {
                    var step = (long x) => x + 1;
                    return step(1);
                }
            }
            new P().Run();
            """);

        await Assert.That(ex.Message).Contains("VM compile rejected")
            .Because($"the failure should be a compile reject, was: {ex.Message}");
        await Assert.That(ex.Message).Contains("has no compilable body")
            .Because($"the reject should name the method that failed, was: {ex.Message}");
        await Assert.That(ex.Message).DoesNotContain("does not define method")
            .Because($"it must not be reported as a missing method on the storage type, was: {ex.Message}");
    }

    /// <summary>A wrong-arity call used to reach the backing dictionary: <c>this.Add(1, 2)</c>
    /// against <c>Add(long)</c> silently became <c>Dictionary.Add</c>. It must not.</summary>
    [Test, Timeout(10_000)]
    public async Task WrongArity_OnVmConstructedInstance_DoesNotReachTheBackingStore(CancellationToken ct) {
        _ = ct;
        var ex = CompileOrRun("""
            class P {
                P() { }
                long Add(long a) { return a; }
                long Run() { return this.Add(1, 2); }
            }
            new P().Run();
            """);

        await Assert.That(ex.Message).DoesNotContain("does not define method")
            .Because($"a wrong-arity call must not fall through to a host lookup, was: {ex.Message}");
        await Assert.That(ex.Message.Contains("VM compile rejected") || ex.Message.Contains("parameter(s)"))
            .IsTrue()
            .Because($"the reject should name the arity problem, was: {ex.Message}");
    }

    /// <summary>Where the receiver is not the VM's own instance — a parameter, which a
    /// host may fill with an object that really does implement the method — the fallback
    /// stays, and the failure now carries the body reject that sent the call there.</summary>
    [Test, Timeout(10_000)]
    public async Task RejectedBody_OnTypedParameterReceiver_CarriesTheReason(CancellationToken ct) {
        _ = ct;
        var ex = CompileOrRun("""
            class Repo {
                Repo() { }
                long Bump(long by) {
                    var step = (long x) => x + 1;
                    return step(by);
                }
            }
            class Use {
                Use() { }
                long Run(Repo r) { return r.Bump(1); }
            }
            new Use().Run(new Repo());
            """);

        await Assert.That(ex.Message).Contains("does not define method 'Bump'")
            .Because($"the name-dispatched fallback is still the contract, was: {ex.Message}");
        await Assert.That(ex.Message).Contains("the AST method body did not compile")
            .Because($"the failure must carry the reject that caused the fallback, was: {ex.Message}");
    }

    /// <summary>Nothing changes for a receiver that resolves to a CLR type: its runtime
    /// type is its API, so the reflection fallback stays available.</summary>
    [Test, Timeout(10_000)]
    public async Task ClrReceiver_StillResolvesThroughTheFallback(CancellationToken ct) {
        _ = ct;
        var result = Run("""
            class P {
                P() { }
                long Run() { return "hello".IndexOf('l'); }
            }
            new P().Run();
            """);

        await Assert.That(result).IsEqualTo(2L);
    }
}