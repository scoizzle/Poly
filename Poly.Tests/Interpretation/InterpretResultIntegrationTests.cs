using Poly.Interpretation;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.Vm;

namespace Poly.Tests.Interpretation;

/// <summary>
/// Integration tests for INT-002: Verify that integer results are not
/// mistaken for heap handles when the heap is populated.
/// </summary>
public class InterpretResultIntegrationTests {
    [Test]
    public async Task ScalarResult_WithPopulatedHeap_ReturnsRawValue() {
        // Build a program whose root expression is Add(1,1) — produces StackScalar.
        // Pre-populate the heap with objects so handle 2 is a valid index.
        var node = new Add(new Constant(1), new Constant(1));

        // Compile via standard pipeline (includes ValueRepresentationAnalysis)
        var program = Interpreter.Compile(node, CompilationMode.Normal);

        // Execute with heap pre-seeded so handle 2 would be a valid object reference.
        // Pre-populate 3 heap entries BEFORE the delegate runs, making handle 2
        // a valid heap index. If InterpretResult used the old heuristic, it would
        // dereference handle 2 as a heap object instead of returning the scalar 2.
        var result = Interpreter.Execute(program, s => {
            s.Heap.Allocate("obj0"); // handle 0
            s.Heap.Allocate("obj1"); // handle 1
            s.Heap.Allocate("obj2"); // handle 2 — would be valid for old heuristic
        });

        // Precondition: heap must have at least 3 entries for this test to be meaningful
        await Assert.That(result.State.Heap.Count).IsGreaterThan(2);

        // Result should be 2 (scalar), not a heap object at handle 2
        await Assert.That(result.HasValue).IsTrue();
        await Assert.That(result.GetValue<int>()).IsEqualTo(2);
        await Assert.That(result.RawValue).IsEqualTo(2L);

        // Confirm the result is a primitive long, not a dereferenced heap object
        await Assert.That(result.Result.Value).IsTypeOf<long>();
    }

    [Test]
    public async Task HeapResult_WithPopulatedHeap_DereferencesCorrectly() {
        // A program returning a string should be classified as HeapRef and
        // InterpretResult should dereference the handle automatically.
        var node = new Constant("hello-world");

        var program = Interpreter.Compile(node, CompilationMode.Normal);
        var result = Interpreter.Execute(program);

        await Assert.That(result.HasValue).IsTrue();
        await Assert.That(result.GetValue<string>()).IsEqualTo("hello-world");
    }

    [Test]
    public async Task BoolResult_WithPopulatedHeap_ReturnsBool() {
        var node = new Equal(new Constant(1), new Constant(1));

        var program = Interpreter.Compile(node, CompilationMode.Normal);
        var result = Interpreter.Execute(program);

        await Assert.That(result.HasValue).IsTrue();
        // True is handle 1 — the old heuristic would have dereferenced handle 1
        // as a heap object if heap.Count > 1
        await Assert.That(result.GetValue<bool>()).IsTrue();
        await Assert.That(result.RawValue).IsEqualTo(1L);
    }

    [Test]
    public async Task BlockRootedScalar_WithPopulatedHeap_ReturnsRawValue() {
        var node = new Block(new Add(new Constant(1), new Constant(1)));
        var program = Interpreter.Compile(node, CompilationMode.Normal);

        await Assert.That(program.RootValueKind).IsEqualTo(ValueRepresentationKind.StackScalar);

        var result = Interpreter.Execute(program, s => {
            s.Heap.Allocate("a");
            s.Heap.Allocate("b");
            s.Heap.Allocate("c");
        });

        await Assert.That(result.State.Heap.Count).IsGreaterThan(2);
        await Assert.That(result.GetValue<int>()).IsEqualTo(2);
        await Assert.That(result.RawValue).IsEqualTo(2L);
    }

    [Test]
    public async Task NullConstant_ReturnsAsScalar() {
        // Null constants are classified StackScalar (0L sentinel).
        var node = new Constant(null);

        var program = Interpreter.Compile(node, CompilationMode.Normal);
        var result = Interpreter.Execute(program);

        await Assert.That(result.HasValue).IsTrue();
        await Assert.That(result.GetValue<long>()).IsEqualTo(0L);
    }

    [Test]
    public async Task StandardPipeline_SetsRootValueKind() {
        // The standard pipeline always stamps ValueRepresentationMetadata,
        // so VmProgram.RootValueKind must be set.
        var scalar = new Add(new Constant(1), new Constant(2));
        var progScalar = Interpreter.Compile(scalar, CompilationMode.Normal);
        await Assert.That(progScalar.RootValueKind).IsNotNull();
        await Assert.That(progScalar.RootValueKind!.Value).IsEqualTo(ValueRepresentationKind.StackScalar);

        var heapExpr = new Constant("hello");
        var progHeap = Interpreter.Compile(heapExpr, CompilationMode.Normal);
        await Assert.That(progHeap.RootValueKind).IsNotNull();
        await Assert.That(progHeap.RootValueKind!.Value).IsEqualTo(ValueRepresentationKind.HeapRef);

        var boolExpr = new Equal(new Constant(1), new Constant(1));
        var progBool = Interpreter.Compile(boolExpr, CompilationMode.Normal);
        await Assert.That(progBool.RootValueKind).IsNotNull();
        await Assert.That(progBool.RootValueKind!.Value).IsEqualTo(ValueRepresentationKind.Bool);
    }
}