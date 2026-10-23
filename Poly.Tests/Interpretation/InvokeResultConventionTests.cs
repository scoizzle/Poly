using Poly.Interpretation;
using Poly.Interpretation.Analysis;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Introspection;

namespace Poly.Tests.Interpretation;

/// <summary>
/// Regression tests for the two defects IS5 fixed while unifying the invoke result
/// convention: a name-dispatched <c>double</c> result lost its IEEE bit pattern, and an
/// inline SetArgs invoke restored the frame pointer to the root sentinel instead of the
/// caller's.
/// </summary>
public class InvokeResultConventionTests {
    public sealed class FractionSource {
        public double Half() => 2.5;
    }

    /// <summary>A type definition declaring <c>Half()</c> returning <c>double</c> with no
    /// body: analysis resolves the member, emit cannot compile its body, so the call takes
    /// the name-dispatched path to the live instance.</summary>
    private static ITypeDefinitionProvider HalfTypeDef() {
        var typeDef = new TypeDefinitionNode(
            "Fraction",
            "Sample",
            Methods: [
                new MethodDefinitionNode(
                    "Half",
                    new PrimitiveTypeReference(PrimitiveType.Float64),
                    Body: new Block([]))
            ]);
        var analyzer = new TypeDefinitionNodeAnalyzer();
        var ctx = AnalysisContext.CreateDefault();
        analyzer.Analyze(ctx, typeDef);
        return ctx.TypeDefinitions;
    }

    [Test]
    public async Task NameDispatchedDoubleReturn_KeepsItsIeeeBits() {
        var invoke = new Invoke(
            new Member(new Parameter("entity", new TypeReference("Sample.Fraction")), "Half"));
        using var exec = Interpreter.Execute(
            Interpreter.Compile(invoke, HalfTypeDef()),
            s => s.SetArgs(new object?[] { new FractionSource() }));

        // 2.5 has a fractional part, so truncation is visible: the pre-fix inline arm
        // converted the returned double to long and the slot held 2, which decodes as
        // Int64BitsToDouble(2) ~ 1e-323. The bound is the bit pattern FromClr produces.
        await Assert.That(exec.RawValue).IsEqualTo(BitConverter.DoubleToInt64Bits(2.5));
        await Assert.That(exec.GetValue<double>()).IsEqualTo(2.5);
    }

    [Test]
    public async Task InlineSetArgsInvokeInsideFunctionBody_KeepsParameterAddressing() {
        var seed = new Parameter("seed", TypeReference.To<long>());
        var x = new Parameter("x", TypeReference.To<long>());
        var q = new Parameter("q", TypeReference.To<long>());
        // The SetArgs shape: a lambda with a parameter invoked with no arguments.
        var inner = new Lambda([q], new Constant(7L));
        // x is read before the inline SetArgs body, which is what keeps that frame live,
        // and read again after it.
        var fn = new Lambda([x],
            new Add(new Multiply(x, new Constant(10L)), new Add(new Invoke(inner), x)));
        var f = new Variable("f");
        var node = new Block([
            seed,
            new Assignment(f, fn),
            new Invoke(f, new Constant(5L))
        ], [f]);

        using var exec = Interpreter.Execute(
            Interpreter.Compile(node), s => s.SetArgs(new object?[] { 999L }));

        // 5 * 10 + (7 + 5). With the root sentinel restored into _fp, both the late read of
        // x and the callee's own result flush use slot 0 as their base, so the invoke
        // answered with the argument slot instead: 5.
        await Assert.That(exec.RawValue).IsEqualTo(62L);
    }
}