using Poly.Interpretation;
using Poly.Interpretation.Analysis;
using Poly.Interpretation.Analysis.ConstantFolding;
using Poly.Interpretation.Analysis.ControlFlow;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.VirtualMachine;
using Poly.Syntax;
using Poly.Syntax.Analysis;
using Poly.Syntax.Nodes;

namespace Poly.Tests.Interpretation;

public class VmSkeletonTests {
    private static byte Op(OpCode op) => (byte)op;

    private static byte[] Int64(long value) =>
        [(byte)(value & 0xFF), (byte)((value >> 8) & 0xFF), (byte)((value >> 16) & 0xFF), (byte)((value >> 24) & 0xFF),
         (byte)((value >> 32) & 0xFF), (byte)((value >> 40) & 0xFF), (byte)((value >> 48) & 0xFF), (byte)((value >> 56) & 0xFF)];

    private static byte[] J(OpCode op, long data) =>
        [(byte)((byte)op | OpCodeEncoding.SizeBit), .. Int64(data)];

    private static byte[] J(OpCode op, int data) => J(op, (long)data);

    [Test]
    public async Task Heap_AllocateGetSet_Roundtrips() {
        var heap = new Heap();
        var h = heap.Allocate("hello");
        await Assert.That(heap.Get(h)).IsEqualTo("hello");

        heap.Set(h, 42);
        await Assert.That(heap.Get(h)).IsEqualTo(42);
        await Assert.That(heap.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Bytecode_ConstructsAndMapsNodeIds() {
        var code = new byte[] { Op(OpCode.Pop) };
        var id = NodeId.NewId();
        var map = new Dictionary<int, NodeId> { [0] = id };

        var program = new Bytecode(code, map);

        await Assert.That(program.CodeLength).IsEqualTo(1);
        await Assert.That(program.GetNodeIdForInstruction(0)).IsEqualTo(id);
    }

    [Test]
    public async Task VmState_Defaults() {
        using var state = new VmState();
        await Assert.That(state.Stack).IsNotNull();
        await Assert.That(state.Heap).IsNotNull();
        await Assert.That(state.FrameBase).IsEqualTo(-1);
        await Assert.That(state.PC).IsEqualTo(0);
    }

    [Test]
    public async Task Vm_Execute_NoProgram_ReturnsVoidAndCompletes() {
        using var state = new VmState();
        var result = Vm.Execute(state);

        await Assert.That(state.IsComplete).IsTrue();
    }

    [Test]
    public async Task Vm_PushPop_StackMatches() {
        var prog = new Bytecode([
            .. J(OpCode.Push, 1),
            .. J(OpCode.Push, 2),
        ], []);
        using var state = new VmState { Program = prog };
        Vm.Execute(state);

        await Assert.That(state.Stack.SP).IsEqualTo(2);
        await Assert.That(state.Stack.Pop()).IsEqualTo(2);
        await Assert.That(state.Stack.Pop()).IsEqualTo(1);
    }

    [Test]
    public async Task Vm_Dup_DuplicatesTop() {
        var prog = new Bytecode([
            .. J(OpCode.Push, 42),
            Op(OpCode.Dup),
        ], []);
        using var state = new VmState { Program = prog };
        Vm.Execute(state);

        await Assert.That(state.Stack.SP).IsEqualTo(2);
        await Assert.That(state.Stack.Pop()).IsEqualTo(42);
        await Assert.That(state.Stack.Pop()).IsEqualTo(42);
    }

    [Test]
    public async Task Vm_Add_ComputesCorrectly() {
        var prog = new Bytecode([
            .. J(OpCode.Push, 10),
            .. J(OpCode.Push, 20),
            Op(OpCode.Add),
        ], []);
        using var state = new VmState { Program = prog };
        Vm.Execute(state);

        await Assert.That(state.Stack.SP).IsEqualTo(1);
        await Assert.That(state.Stack.Pop()).IsEqualTo(30);
    }

    [Test]
    public async Task Vm_Sub_ComputesCorrectly() {
        var prog = new Bytecode([
            .. J(OpCode.Push, 100),
            .. J(OpCode.Push, 30),
            Op(OpCode.Sub),
        ], []);
        using var state = new VmState { Program = prog };
        Vm.Execute(state);

        await Assert.That(state.Stack.Pop()).IsEqualTo(70);
    }

    [Test]
    public async Task Vm_Mul_ComputesCorrectly() {
        var prog = new Bytecode([
            .. J(OpCode.Push, 7),
            .. J(OpCode.Push, 6),
            Op(OpCode.Mul),
        ], []);
        using var state = new VmState { Program = prog };
        Vm.Execute(state);

        await Assert.That(state.Stack.Pop()).IsEqualTo(42);
    }

    [Test]
    public async Task Vm_Div_ComputesCorrectly() {
        var prog = new Bytecode([
            .. J(OpCode.Push, 42),
            .. J(OpCode.Push, 7),
            Op(OpCode.Div),
        ], []);
        using var state = new VmState { Program = prog };
        Vm.Execute(state);

        await Assert.That(state.Stack.Pop()).IsEqualTo(6);
    }

    [Test]
    public async Task Vm_Neg_ProducesNegative() {
        var prog = new Bytecode([
            .. J(OpCode.Push, 42),
            Op(OpCode.Neg),
        ], []);
        using var state = new VmState { Program = prog };
        Vm.Execute(state);

        await Assert.That(state.Stack.Pop()).IsEqualTo(-42);
    }

    [Test]
    public async Task Vm_Not_InvertsZero() {
        var prog = new Bytecode([
            .. J(OpCode.Push, 0),
            Op(OpCode.Not),
        ], []);
        using var state = new VmState { Program = prog };
        Vm.Execute(state);

        await Assert.That(state.Stack.Pop()).IsEqualTo(1);
    }

    [Test]
    public async Task Vm_Comparisons_ProduceCorrectValues() {
        var prog = new Bytecode([
            .. J(OpCode.Push, 5),
            .. J(OpCode.Push, 10),
            Op(OpCode.Lt),
            .. J(OpCode.Push, 5),
            .. J(OpCode.Push, 10),
            Op(OpCode.Gt),
        ], []);
        using var state = new VmState { Program = prog };
        Vm.Execute(state);

        await Assert.That(state.Stack.Pop()).IsEqualTo(0); // 5 > 10 = false
        await Assert.That(state.Stack.Pop()).IsEqualTo(1); // 5 < 10 = true
    }

    [Test]
    public async Task Vm_Eq_Ne_ComputeCorrectly() {
        var prog = new Bytecode([
            .. J(OpCode.Push, 5),
            .. J(OpCode.Push, 5),
            Op(OpCode.Eq),
            .. J(OpCode.Push, 5),
            .. J(OpCode.Push, 3),
            Op(OpCode.Ne),
        ], []);
        using var state = new VmState { Program = prog };
        Vm.Execute(state);

        await Assert.That(state.Stack.Pop()).IsEqualTo(1); // 5 != 3 = true
        await Assert.That(state.Stack.Pop()).IsEqualTo(1); // 5 == 5 = true
    }

    [Test]
    public async Task Vm_Bitwise_Ops_ComputeCorrectly() {
        var prog = new Bytecode([
            .. J(OpCode.Push, 5), .. J(OpCode.Push, 2), Op(OpCode.BitOr),
            .. J(OpCode.Push, 3), Op(OpCode.BitAnd),
        ], []);
        using var state = new VmState { Program = prog };
        Vm.Execute(state);

        await Assert.That(state.Stack.Pop()).IsEqualTo(3); // (5|2) & 3 = 7 & 3 = 3
    }

    [Test]
    public async Task Vm_Shifts_ComputeCorrectly() {
        var prog = new Bytecode([
            .. J(OpCode.Push, 8), .. J(OpCode.Push, 1), Op(OpCode.Shl),
            .. J(OpCode.Push, 1), Op(OpCode.Shr),
        ], []);
        using var state = new VmState { Program = prog };
        Vm.Execute(state);

        // 8 << 1 = 16, then 16 >> 1 = 8
        await Assert.That(state.Stack.Pop()).IsEqualTo(8);
    }

    [Test]
    public async Task Vm_DivRem_SingleOpcode() {
        var prog = new Bytecode([
            .. J(OpCode.Push, 17),
            .. J(OpCode.Push, 5),
            Op(OpCode.DivRem),
        ], []);
        using var state = new VmState { Program = prog };
        Vm.Execute(state);

        await Assert.That(state.Stack.Pop()).IsEqualTo(2); // 17 % 5
        await Assert.That(state.Stack.Pop()).IsEqualTo(3); // 17 / 5
    }

    [Test]
    public async Task Vm_Jump_BranchesToTarget() {
        var prog = new Bytecode([
            .. J(OpCode.Push, 1),
            .. J(OpCode.Jump, 3),           // jump to chunk 3 (skip chunk 2's Push 99)
            .. J(OpCode.Push, 99),          // chunk 2: skipped
            .. J(OpCode.Push, 2),            // chunk 3
        ], []);
        using var state = new VmState { Program = prog };
        Vm.Execute(state);

        await Assert.That(state.Stack.SP).IsEqualTo(2);
        await Assert.That(state.Stack.Pop()).IsEqualTo(2);
        await Assert.That(state.Stack.Pop()).IsEqualTo(1);
    }

    [Test]
    public async Task Vm_JumpIfFalse_SkipsOnZero() {
        var prog = new Bytecode([
            .. J(OpCode.Push, 0),           // false
            .. J(OpCode.JumpIfFalse, 3),    // jump to chunk 3 (skip chunk 2's Push 99)
            .. J(OpCode.Push, 99),           // chunk 2: skipped
            .. J(OpCode.Push, 42),
        ], []);
        using var state = new VmState { Program = prog };
        Vm.Execute(state);

        await Assert.That(state.Stack.SP).IsEqualTo(1);
        await Assert.That(state.Stack.Pop()).IsEqualTo(42);
    }

    [Test]
    public async Task Vm_FullPipeline_Polynomial() {
        // AST: 3*5*5*5 + 2*5*5 + 5 + 5 = 435
        var node = new Add(
            new Add(
                new Add(
                    new Multiply(new Constant(3),
                        new Multiply(new Constant(5), new Multiply(new Constant(5), new Constant(5)))),
                    new Multiply(new Constant(2), new Multiply(new Constant(5), new Constant(5)))
                ),
                new Constant(5)
            ),
            new Constant(5)
        );

        var analysis = new AnalyzerBuilder()
            .UseTypeResolver()
            .UseMemberResolver()
            .UseConstantFolding()
            .UseSideEffectAnalysis()
            .Build()
            .Analyze(node);

        var program = Lowering.Lower(node, analysis);

        using var state = new VmState { Program = program };
        var result = Vm.Execute(state);

        await Assert.That(state.IsComplete).IsTrue();
        await Assert.That(result.Value).IsEqualTo(435L);
    }

    [Test]
    public async Task Vm_FullPipeline_SimpleMathCall() {
        // Call Math.Max(3, 7) — should return 7
        var maxMethod = new Member(
            new TypeReference(typeof(Math).FullName!),
            nameof(Math.Max)
        );
        var invoke = new Invoke(maxMethod, new Constant(3), new Constant(7));

        var analysis = new AnalyzerBuilder()
            .UseTypeResolver()
            .UseMemberResolver()
            .Build()
            .Analyze(invoke);

        var program = Lowering.Lower(invoke, analysis);
        using var state = new VmState { Program = program };
        var result = Vm.Execute(state);

        await Assert.That(state.IsComplete).IsTrue();
        await Assert.That(result.Value).IsEqualTo(7L);
    }

    [Test]
    public async Task Vm_FullPipeline_SingleArgCall() {
        var absMethod = new Member(
            new TypeReference(typeof(Math).FullName!),
            nameof(Math.Abs)
        );
        var invoke = new Invoke(absMethod, new Constant(-5));

        var analysis = new AnalyzerBuilder()
            .UseTypeResolver()
            .UseMemberResolver()
            .Build()
            .Analyze(invoke);

        var program = Lowering.Lower(invoke, analysis);
        using var state = new VmState { Program = program };
        var result = Vm.Execute(state);

        await Assert.That(state.IsComplete).IsTrue();
        await Assert.That(result.Value).IsEqualTo(5L);
    }

    [Test]
    public async Task Vm_FullPipeline_WithFunctionCall() {
        var maxMethod = new Member(
            new TypeReference(typeof(Math).FullName!),
            nameof(Math.Max)
        );
        var invoke = new Invoke(maxMethod, new Constant(3), new Constant(7));

        var analysis = new AnalyzerBuilder()
            .UseTypeResolver()
            .UseMemberResolver()
            .Build()
            .Analyze(invoke);

        var program = Lowering.Lower(invoke, analysis);
        using var state = new VmState { Program = program };
        var result = Vm.Execute(state);

        // Math.Max(3, 7) = 7
        await Assert.That(state.IsComplete).IsTrue();
        await Assert.That(result.Value).IsEqualTo(7L);
    }
}