using Poly.Interpretation.Analysis;
using Poly.Interpretation.Analysis.ConstantFolding;
using Poly.Interpretation.Analysis.ControlFlow;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Syntax.Analysis;
using Poly.Syntax.Nodes;

namespace Poly.Tests.Interpretation;

public class ValueRepresentationTests {
    private static readonly Analyzer AnalyzerWithPass = new AnalyzerBuilder()
        .UseTypeAndMemberResolver()
        .UseVariableScopeValidator()
        .UseSideEffectAnalysis()
        .UseThisReferenceContext()
        .UseJumpTargetResolution()
        .UseControlFlowAnalysis()
        .UseValueRepresentationAnalysis()
        .Build();

    [Test]
    public async Task Block_PropagatesLastExpressionKind() {
        var node = new Block(new Constant(1), new Add(new Constant(2), new Constant(3)));
        var result = AnalyzerWithPass.Analyze(node);
        var meta = result.GetMetadata<ValueRepresentationMetadata>(node);

        await Assert.That(meta).IsNotNull();
        await Assert.That(meta!.Kind).IsEqualTo(ValueRepresentationKind.StackScalar);
    }

    [Test]
    public async Task IntConstant_IsStackScalar() {
        var node = new Constant(42);
        var result = AnalyzerWithPass.Analyze(node);
        var meta = result.GetMetadata<ValueRepresentationMetadata>(node);

        await Assert.That(meta).IsNotNull();
        await Assert.That(meta!.Kind).IsEqualTo(ValueRepresentationKind.StackScalar);
    }

    [Test]
    public async Task BoolConstant_IsBool() {
        var node = new Constant(true);
        var result = AnalyzerWithPass.Analyze(node);
        var meta = result.GetMetadata<ValueRepresentationMetadata>(node);

        await Assert.That(meta).IsNotNull();
        await Assert.That(meta!.Kind).IsEqualTo(ValueRepresentationKind.Bool);
    }

    [Test]
    public async Task StringConstant_IsHeapRef() {
        var node = new Constant("hello");
        var result = AnalyzerWithPass.Analyze(node);
        var meta = result.GetMetadata<ValueRepresentationMetadata>(node);

        await Assert.That(meta).IsNotNull();
        await Assert.That(meta!.Kind).IsEqualTo(ValueRepresentationKind.HeapRef);
    }

    [Test]
    public async Task NullConstant_IsStackScalar() {
        // Null is represented as 0L sentinel on the VM stack
        var node = new Constant(null);
        var result = AnalyzerWithPass.Analyze(node);
        var meta = result.GetMetadata<ValueRepresentationMetadata>(node);

        await Assert.That(meta).IsNotNull();
        await Assert.That(meta!.Kind).IsEqualTo(ValueRepresentationKind.StackScalar);
    }

    [Test]
    public async Task Block_LastExpressionDeterminesKind() {
        var node = new Block(Wrap(1), Wrap(2));
        var result = AnalyzerWithPass.Analyze(node);
        var meta = result.GetMetadata<ValueRepresentationMetadata>(node);

        await Assert.That(meta).IsNotNull();
        await Assert.That(meta!.Kind).IsEqualTo(ValueRepresentationKind.StackScalar);
    }

    [Test]
    public async Task Arithmetic_IsStackScalar() {
        var node = new Add(new Constant(1), new Constant(2));
        var result = AnalyzerWithPass.Analyze(node);
        var meta = result.GetMetadata<ValueRepresentationMetadata>(node);

        await Assert.That(meta).IsNotNull();
        await Assert.That(meta!.Kind).IsEqualTo(ValueRepresentationKind.StackScalar);
    }

    [Test]
    public async Task EqualityComparison_IsBool() {
        var node = new Equal(new Constant(1), new Constant(2));
        var result = AnalyzerWithPass.Analyze(node);
        var meta = result.GetMetadata<ValueRepresentationMetadata>(node);

        await Assert.That(meta).IsNotNull();
        await Assert.That(meta!.Kind).IsEqualTo(ValueRepresentationKind.Bool);
    }

    [Test]
    public async Task BooleanAnd_IsBool() {
        var node = new And(new Constant(true), new Constant(false));
        var result = AnalyzerWithPass.Analyze(node);
        var meta = result.GetMetadata<ValueRepresentationMetadata>(node);

        await Assert.That(meta).IsNotNull();
        await Assert.That(meta!.Kind).IsEqualTo(ValueRepresentationKind.Bool);
    }

    [Test]
    public async Task IfStatement_IsVoid() {
        var node = new IfStatement(new Constant(true), Wrap(1), Wrap(2));
        var result = AnalyzerWithPass.Analyze(node);
        var meta = result.GetMetadata<ValueRepresentationMetadata>(node);

        await Assert.That(meta).IsNotNull();
        await Assert.That(meta!.Kind).IsEqualTo(ValueRepresentationKind.Void);
    }

    [Test]
    public async Task WhileLoop_IsVoid() {
        var node = new WhileLoop(new Constant(true), Wrap(1));
        var result = AnalyzerWithPass.Analyze(node);
        var meta = result.GetMetadata<ValueRepresentationMetadata>(node);

        await Assert.That(meta).IsNotNull();
        await Assert.That(meta!.Kind).IsEqualTo(ValueRepresentationKind.Void);
    }

    [Test]
    public async Task Not_IsBool() {
        var node = new Not(new Constant(true));
        var result = AnalyzerWithPass.Analyze(node);
        var meta = result.GetMetadata<ValueRepresentationMetadata>(node);

        await Assert.That(meta).IsNotNull();
        await Assert.That(meta!.Kind).IsEqualTo(ValueRepresentationKind.Bool);
    }

    [Test]
    public async Task UnaryMinus_IsStackScalar() {
        var node = new UnaryMinus(new Constant(5));
        var result = AnalyzerWithPass.Analyze(node);
        var meta = result.GetMetadata<ValueRepresentationMetadata>(node);

        await Assert.That(meta).IsNotNull();
        await Assert.That(meta!.Kind).IsEqualTo(ValueRepresentationKind.StackScalar);
    }

    [Test]
    public async Task ThrowStatement_IsVoid() {
        var node = new ThrowStatement(new Constant("error"));
        var result = AnalyzerWithPass.Analyze(node);
        var meta = result.GetMetadata<ValueRepresentationMetadata>(node);

        await Assert.That(meta).IsNotNull();
        await Assert.That(meta!.Kind).IsEqualTo(ValueRepresentationKind.Void);
    }

    [Test]
    public async Task Return_IsVoid() {
        var node = new Return(new Constant(42));
        var result = AnalyzerWithPass.Analyze(node);
        var meta = result.GetMetadata<ValueRepresentationMetadata>(node);

        await Assert.That(meta).IsNotNull();
        await Assert.That(meta!.Kind).IsEqualTo(ValueRepresentationKind.Void);
    }

    [Test]
    public async Task NewArray_IsHeapRef() {
        var node = new NewArray(
            TypeReference.To<int>(),
            new Constant(10));
        var result = AnalyzerWithPass.Analyze(node);
        var meta = result.GetMetadata<ValueRepresentationMetadata>(node);

        await Assert.That(meta).IsNotNull();
        await Assert.That(meta!.Kind).IsEqualTo(ValueRepresentationKind.HeapRef);
    }

    [Test]
    public async Task Coalesce_IntConstants_IsStackScalar() {
        var node = new Coalesce(new Constant(42), new Constant(99));
        var result = AnalyzerWithPass.Analyze(node);
        var meta = result.GetMetadata<ValueRepresentationMetadata>(node);

        await Assert.That(meta).IsNotNull();
        await Assert.That(meta!.Kind).IsEqualTo(ValueRepresentationKind.StackScalar);
    }

    [Test]
    public async Task Conditional_IntBranches_IsStackScalar() {
        var node = new Conditional(new Constant(true), new Constant(1), new Constant(2));
        var result = AnalyzerWithPass.Analyze(node);
        var meta = result.GetMetadata<ValueRepresentationMetadata>(node);

        await Assert.That(meta).IsNotNull();
        await Assert.That(meta!.Kind).IsEqualTo(ValueRepresentationKind.StackScalar);
    }

    [Test]
    public async Task Add_ResolvedType_HasClrType() {
        var node = new Add(new Constant(1), new Constant(2));
        var result = AnalyzerWithPass.Analyze(node);
        var meta = result.GetMetadata<ValueRepresentationMetadata>(node);

        await Assert.That(meta).IsNotNull();
        await Assert.That(meta!.Kind).IsEqualTo(ValueRepresentationKind.StackScalar);
        await Assert.That(meta.ClrType).IsNotNull();
    }

    [Test]
    public async Task ForEachLoop_IsVoid() {
        var variable = new Variable("item");
        var node = new ForEachLoop(variable, new Constant(new int[0]), Wrap(1));
        var result = AnalyzerWithPass.Analyze(node);
        var meta = result.GetMetadata<ValueRepresentationMetadata>(node);

        await Assert.That(meta).IsNotNull();
        await Assert.That(meta!.Kind).IsEqualTo(ValueRepresentationKind.Void);
    }

    [Test]
    public async Task Conditional_HeapBranches_IsHeapRef() {
        var node = new Conditional(new Constant(true), new Constant("hello"), new Constant("world"));
        var result = AnalyzerWithPass.Analyze(node);
        var meta = result.GetMetadata<ValueRepresentationMetadata>(node);

        await Assert.That(meta).IsNotNull();
        await Assert.That(meta!.Kind).IsEqualTo(ValueRepresentationKind.HeapRef);
    }

    [Test]
    public async Task Coalesce_String_IsHeapRef() {
        var node = new Coalesce(new Constant("hello"), new Constant("default"));
        var result = AnalyzerWithPass.Analyze(node);
        var meta = result.GetMetadata<ValueRepresentationMetadata>(node);

        await Assert.That(meta).IsNotNull();
        await Assert.That(meta!.Kind).IsEqualTo(ValueRepresentationKind.HeapRef);
    }

    private static Node Wrap(int value) => new Constant(value);

    [Test]
    public async Task Member_IntProperty_IsStackScalar() {
        var node = new Member(new Constant("hello"), "Length");
        var result = AnalyzerWithPass.Analyze(node);
        var meta = result.GetMetadata<ValueRepresentationMetadata>(node);

        await Assert.That(meta).IsNotNull();
        await Assert.That(meta!.Kind).IsEqualTo(ValueRepresentationKind.StackScalar);
        await Assert.That(meta.ClrType).IsNotNull();
    }

    [Test]
    public async Task Member_RefReturningProperty_IsHeapRef() {
        var node = new Member(new Constant("hello"), "ToUpper");
        var result = AnalyzerWithPass.Analyze(node);
        var meta = result.GetMetadata<ValueRepresentationMetadata>(node);

        await Assert.That(meta).IsNotNull();
        await Assert.That(meta!.Kind).IsEqualTo(ValueRepresentationKind.HeapRef);
        await Assert.That(meta.ClrType).IsEqualTo(typeof(string));
    }
}