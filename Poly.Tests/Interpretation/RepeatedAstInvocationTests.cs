using Poly.Interpretation;
using Poly.Interpretation.CSharp;
using Poly.Interpretation.Vm;

namespace Poly.Tests.Interpretation;

/// <summary>
/// A body may invoke an AST method more than once. Two invokes used to make the whole
/// program lose its type: <c>new P().Run()</c> then resolved as a host call on the
/// instance dictionary and failed with
/// <c>Type 'Dictionary`2' does not define method 'Run'</c>.
/// </summary>
public class RepeatedAstInvocationTests {
    private static long Run(string source) {
        var unit = CSharpLanguage.Read(source);
        using var exec = Interpreter.Execute(
            Interpreter.Compile(unit.TopLevelStatements![0], Interpreter.Analyze(unit), CompilationMode.NoDebug));
        return exec.RawValue;
    }

    [Test, Timeout(10_000)]
    public async Task TwoInvocations_InOneExpression_UseTheAstMethod(CancellationToken ct) {
        _ = ct;
        var result = Run("""
            class P {
                P() { }
                long Add(long a, long b) { return a + b; }
                long Run() { return this.Add(1, 2) + this.Add(3, 4); }
            }
            new P().Run();
            """);

        await Assert.That(result).IsEqualTo(10L);
    }

    [Test, Timeout(10_000)]
    public async Task TwoInvocations_OfDifferentMethods_UseTheAstMethods(CancellationToken ct) {
        _ = ct;
        var result = Run("""
            class P {
                P() { }
                long A(long v) { return v + 1; }
                long B(long v) { return v + 2; }
                long Run() { return this.A(1) + this.B(2); }
            }
            new P().Run();
            """);

        await Assert.That(result).IsEqualTo(6L);
    }

    [Test, Timeout(10_000)]
    public async Task TwoInvocations_AsSeparateStatements_UseTheAstMethod(CancellationToken ct) {
        _ = ct;
        var result = Run("""
            class P {
                P() { }
                long Add(long a, long b) { return a + b; }
                long Run() {
                    var first = this.Add(1, 2);
                    var second = this.Add(3, 4);
                    return first + second;
                }
            }
            new P().Run();
            """);

        await Assert.That(result).IsEqualTo(10L);
    }
}