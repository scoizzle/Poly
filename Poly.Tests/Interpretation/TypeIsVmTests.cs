using Poly.Interpretation;
using Poly.Interpretation.Analysis.ConstantFolding;
using Poly.Interpretation.Analysis.ControlFlow;
using Poly.Interpretation.Analysis.Semantics;

namespace Poly.Tests.Interpretation;

/// <summary>
/// VM-path tests for TypeIs/TypeCheck execution through the full pipeline.
/// Maps to P3-D (TypeIs VM path coverage).
/// </summary>
public class TypeIsVmTests {
    private static readonly Analyzer _analyzer = new AnalyzerBuilder()
        .UseThisReferenceContext()
        .UseVariableScopeValidator()
        .UseTypeAndMemberResolver()
        .UseSideEffectAnalysis()
        .UseJumpTargetResolution()
        .UseConstantFolding()
        .UseControlFlowAnalysis()
        .UseLambdaReturnTypeResolution()
        .UseCallSiteCatalog()
        .UseDefiniteAssignmentAnalysis()
        .UseExceptionRegionAnalysis()
        // .UsePrimitiveExpansion() — deprecated/non-critical
        .Build();

    /// <summary>
    /// TypeIs with a string constant on a string type — should match.
    /// </summary>
    [Test]
    public async Task TypeIs_StringConstant_IsString_ReturnsTrue() {
        var node = new TypeIs(new Constant("hello"), TypeReference.To<string>());
        var analysis = _analyzer.Analyze(node);
        var result = Interpreter.Compile(node, analysis);
        using var exec = Interpreter.Execute(result);
        await Assert.That(exec.RawValue).IsEqualTo(1L);
    }

    /// <summary>
    /// TypeIs with a string constant on an int type — should not match.
    /// </summary>
    [Test]
    public async Task TypeIs_StringConstant_IsInt_ReturnsFalse() {
        var node = new TypeIs(new Constant("hello"), TypeReference.To<int>());
        var analysis = _analyzer.Analyze(node);
        var result = Interpreter.Compile(node, analysis);
        using var exec = Interpreter.Execute(result);
        await Assert.That(exec.RawValue).IsEqualTo(0L);
    }

    /// <summary>
    /// TypeIs with a null constant on any type — should return false.
    /// </summary>
    [Test]
    public async Task TypeIs_NullConstant_ReturnsFalse() {
        var node = new TypeIs(Null, TypeReference.To<string>());
        var analysis = _analyzer.Analyze(node);
        var result = Interpreter.Compile(node, analysis);
        using var exec = Interpreter.Execute(result);
        await Assert.That(exec.RawValue).IsEqualTo(0L);
    }

    /// <summary>
    /// A scalar operand is answered from its static type: an <c>int</c> constant is a
    /// scalar in a ring slot, not a heap handle, so it is <c>is int</c>.
    /// </summary>
    [Test]
    public async Task TypeIs_IntConstant_IsInt_ReturnsTrue() {
        var node = new TypeIs(new Constant(1), TypeReference.To<int>());
        var analysis = _analyzer.Analyze(node);
        var result = Interpreter.Compile(node, analysis);
        using var exec = Interpreter.Execute(result);
        await Assert.That(exec.RawValue).IsEqualTo(1L);
    }

    /// <summary>
    /// <c>is</c> never widens: an <c>int</c> constant is not a <c>long</c>.
    /// </summary>
    [Test]
    public async Task TypeIs_IntConstant_IsLong_ReturnsFalse() {
        var node = new TypeIs(new Constant(1), TypeReference.To<long>());
        var analysis = _analyzer.Analyze(node);
        var result = Interpreter.Compile(node, analysis);
        using var exec = Interpreter.Execute(result);
        await Assert.That(exec.RawValue).IsEqualTo(0L);
    }

    /// <summary>
    /// A scalar operand boxes to its base type: an <c>int</c> constant is a
    /// <see cref="ValueType"/>.
    /// </summary>
    [Test]
    public async Task TypeIs_IntConstant_IsValueType_ReturnsTrue() {
        var node = new TypeIs(new Constant(1), TypeReference.To<ValueType>());
        var analysis = _analyzer.Analyze(node);
        var result = Interpreter.Compile(node, analysis);
        using var exec = Interpreter.Execute(result);
        await Assert.That(exec.RawValue).IsEqualTo(1L);
    }
}