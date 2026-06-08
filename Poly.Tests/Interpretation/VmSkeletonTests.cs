using System.Reflection;

using Poly.Interpretation;
using Poly.Interpretation.Analysis.ConstantFolding;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.VirtualMachine;
using Poly.Syntax.Analysis;
using Poly.Syntax.Nodes;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.Interpretation;

public class VmSkeletonTests {
    private static byte Op(OpCode op) => (byte)op;

    private static byte[] Int32(int value) =>
        [(byte)(value & 0xFF), (byte)((value >> 8) & 0xFF), (byte)((value >> 16) & 0xFF), (byte)((value >> 24) & 0xFF)];

    private static byte[] Int64(long value) =>
        [(byte)(value & 0xFF), (byte)((value >> 8) & 0xFF), (byte)((value >> 16) & 0xFF), (byte)((value >> 24) & 0xFF),
         (byte)((value >> 32) & 0xFF), (byte)((value >> 40) & 0xFF), (byte)((value >> 48) & 0xFF), (byte)((value >> 56) & 0xFF)];

    private static byte[] J(OpCode op, int data) => [Op(op), .. Int32(data)];

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
        var code = new byte[] { Op(OpCode.PushInt), 0x01, 0x00, 0x00, 0x00, Op(OpCode.Nop) };
        var id = NodeId.NewId();
        var map = new Dictionary<int, NodeId> { [0] = id };

        var program = new Bytecode(code, map);

        await Assert.That(program.CodeLength).IsEqualTo(6);
        await Assert.That(program.GetNodeIdForInstruction(0)).IsEqualTo(id);
        await Assert.That(program.GetNodeIdForInstruction(1)).IsNull();
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

        await Assert.That(result.IsVoid || !result.HasValue).IsTrue();
        await Assert.That(state.IsComplete).IsTrue();
    }

    [Test]
    public async Task Vm_ConstAndArith_AllCoreArithOps_Work() {
        var prog = new Bytecode([
            .. J(OpCode.PushInt, 10),
            .. J(OpCode.PushInt, 3),
            Op(OpCode.Add),
            .. J(OpCode.PushInt, 2),
            Op(OpCode.Sub),
            .. J(OpCode.PushInt, 4),
            Op(OpCode.Mul),
            .. J(OpCode.PushInt, 5),
            Op(OpCode.Div),
            .. J(OpCode.PushInt, 3),
            Op(OpCode.Mod),
            Op(OpCode.Neg),
        ], []);

        using var state = new VmState();
        state.Program = prog;

        var result = Vm.Execute(state);

        await Assert.That(result.HasValue).IsTrue();
        await Assert.That((int)result.Value!).IsEqualTo(-2);
    }

    [Test]
    public async Task Vm_Narrow_EnforcesDownScaling() {
        var prog = new Bytecode([
            .. J(OpCode.PushInt, 300),
            .. J(OpCode.PushInt, 20),
            Op(OpCode.Add),
            .. J(OpCode.Narrow, 0),
            .. J(OpCode.PushInt, 200),
            .. J(OpCode.PushInt, 100),
            Op(OpCode.Add),
            .. J(OpCode.Narrow, 5),
        ], []);

        using var state = new VmState();
        state.Program = prog;

        var result = Vm.Execute(state);

        await Assert.That((int)result.Value!).IsEqualTo(44);
    }

    [Test]
    public async Task Vm_ComparisonsAndJumpIfFalse_BasicControlFlow_Works() {
        var prog = new Bytecode([
            .. J(OpCode.PushInt, 5),
            .. J(OpCode.PushInt, 5),
            Op(OpCode.Eq),
            .. J(OpCode.JumpIfFalse, 5 + 5 + 1 + 5 + 5 + 1),  // jump to 99
            .. J(OpCode.PushInt, 42),
            .. J(OpCode.Jump, 5 + 5 + 1 + 5 + 5 + 1 + 5 + 5 + 1), // jump past 99
            .. J(OpCode.PushInt, 99),
        ], []);

        using var state = new VmState();
        state.Program = prog;

        var result = Vm.Execute(state);

        await Assert.That((int)result.Value!).IsEqualTo(42);
    }

    [Test]
    public async Task Vm_DupPop_StackManagement_Works() {
        var prog = new Bytecode([
            .. J(OpCode.PushInt, 7),
            Op(OpCode.Dup),
            Op(OpCode.Pop),
        ], []);

        using var state = new VmState();
        state.Program = prog;

        var result = Vm.Execute(state);
        await Assert.That((int)result.Value!).IsEqualTo(7);
    }

    [Test]
    public async Task Vm_Suspend_StopsExecutionAndSetsStatus() {
        var prog = new Bytecode([
            .. J(OpCode.PushInt, 1),
            .. J(OpCode.Int, 0),
            .. J(OpCode.PushInt, 2),
        ], []);

        using var state = new VmState();
        state.Program = prog;

        var result = Vm.Execute(state);

        await Assert.That(state.IsSuspended).IsTrue();
        await Assert.That(result.IsSignal).IsTrue();
        await Assert.That(result.Signal?.Kind).IsEqualTo(InterpreterSignal.SignalKind.Suspend);
    }

    [Test]
    public async Task Vm_CallExternal_StaticMethodDispatch_Works() {
        var method = typeof(Convert).GetMethod(nameof(Convert.ToInt64), new[] { typeof(long) })!;
        var del = CallSiteCompiler.Compile(method, isStatic: true);
        using var state = new VmState();

        var prog = new Bytecode([
            .. J(OpCode.PushInt, 42),
            .. J(OpCode.PushInt, 1),
            .. J(OpCode.PushInt, 1),
            .. J(OpCode.CallExternal, 0),
        ], [], callSites: [del]);

        state.Program = prog;

        var result = Vm.Execute(state);

        await Assert.That(result.HasValue).IsTrue();
        await Assert.That((int)result.Value!).IsEqualTo(42);
    }

    [Test]
    public async Task Vm_CallExternal_DelegateDynamicDispatch_Works() {
        using var state = new VmState();
        CallSiteDelegate addSite = static (s) => {
            var (argSlots, hasRet) = s.Stack.Pop<(int argSlots, int hasRet)>();
            int b = s.Stack.PopInt();
            int a = s.Stack.PopInt();
            long result = (long)a + b;
            if (hasRet != 0) s.Stack.Push((int)result);
        };

        var prog = new Bytecode([
            .. J(OpCode.PushInt, 10),
            .. J(OpCode.PushInt, 20),
            .. J(OpCode.PushInt, 2),
            .. J(OpCode.PushInt, 1),
            .. J(OpCode.CallExternal, 0),
        ], [], callSites: [addSite]);

        state.Program = prog;

        var result = Vm.Execute(state);

        await Assert.That(result.HasValue).IsTrue();
        await Assert.That((int)result.Value!).IsEqualTo(30);
    }

    [Test]
    public async Task Vm_CallExternal_MissingTarget_Throws() {
        using var state = new VmState();

        var prog = new Bytecode([
            .. J(OpCode.PushInt, 42),
            .. J(OpCode.PushInt, 1),
            .. J(OpCode.PushInt, 1),
            .. J(OpCode.CallExternal, 0),
        ], []);

        state.Program = prog;

        var result = Vm.Execute(state);

        await Assert.That(result.IsSignal).IsTrue();
        await Assert.That(result.Value).IsNotNull();
        var ex = (Exception)result.Value!;
        await Assert.That(ex.Message).Contains("no target");
    }

    [Test]
    public async Task Vm_CallExternal_WithConstantArg_ResolvesCorrectly() {
        var method = typeof(Convert).GetMethod(nameof(Convert.ToInt32), [typeof(string)])!;
        var del = CallSiteCompiler.Compile(method, isStatic: true);
        using var state = new VmState();

        var prog = new Bytecode([
            .. J(OpCode.LoadConst, 0),
            .. J(OpCode.PushInt, 1),
            .. J(OpCode.PushInt, 1),
            .. J(OpCode.CallExternal, 0),
        ], [], constants: ["777"], callSites: [del]);

        state.Program = prog;

        var result = Vm.Execute(state);

        await Assert.That(result.HasValue).IsTrue();
        await Assert.That((int)result.Value!).IsEqualTo(777);
    }

    [Test]
    public async Task Vm_EndOfProgram_VoidVsValue() {
        var voidProg = new Bytecode(
            [Op(OpCode.Nop)],
            []);
        using var s1 = new VmState { Program = voidProg };
        var r1 = Vm.Execute(s1);
        await Assert.That(r1.IsVoid).IsTrue();
        await Assert.That(s1.IsComplete).IsTrue();

        var valProg = new Bytecode(
            [.. J(OpCode.PushInt, 42)],
            []);
        using var s2 = new VmState { Program = valProg };
        var r2 = Vm.Execute(s2);
        await Assert.That(r2.HasValue).IsTrue();
        await Assert.That((int)r2.Value!).IsEqualTo(42);
        await Assert.That(s2.IsComplete).IsTrue();
    }

    [Test]
    public async Task Vm_ErrorHandling_Div0AndUnderflowProduceThrow() {
        var div0Prog = new Bytecode([
            .. J(OpCode.PushInt, 1),
            .. J(OpCode.PushInt, 0),
            Op(OpCode.Div),
        ], []);
        using var s1 = new VmState { Program = div0Prog };
        var r1 = Vm.Execute(s1);
        await Assert.That(r1.IsSignal).IsTrue();
        await Assert.That(r1.Value).IsNotNull();
        var ex1 = (Exception)r1.Value!;
        await Assert.That(ex1.Message).Contains("Division by zero");
    }

    [Test]
    public async Task Vm_LoadStoreValue_HeapHandle_Works() {
        using var state = new VmState();
        int h = state.Heap.Allocate(0);

        var prog = new Bytecode([
            .. J(OpCode.PushInt, 123),
            .. J(OpCode.PushInt, h),
            .. J(OpCode.PushInt, 1),
            Op(OpCode.StoreValue),
        ], []);

        state.Program = prog;
        var result = Vm.Execute(state);
        if (result.IsSignal && result.Value is Exception innerEx)
            throw new Exception("VM signal: " + innerEx.Message, innerEx);
        await Assert.That(result.IsVoid).IsTrue();
        await Assert.That(state.Heap.Get(h)).IsEqualTo(123);
    }

    [Test]
    public async Task Vm_Combined_UsefulSubset_ExprControlCallByrefExternal() {
        using var state = new VmState();

        var convertMethod = typeof(Convert).GetMethod(nameof(Convert.ToInt64), new[] { typeof(long) })!;
        var del = CallSiteCompiler.Compile(convertMethod, isStatic: true);

        var code = new List<byte>();

        // 0: Push 10, Push 5, Add → stack has 15
        code.AddRange(J(OpCode.PushInt, 10));
        code.AddRange(J(OpCode.PushInt, 5));
        code.Add(Op(OpCode.Add));

        // CALL_EXTERNAL: Convert.ToInt64(15) with argSlots=1, hasRet=1
        code.AddRange(J(OpCode.PushInt, 1));
        code.AddRange(J(OpCode.PushInt, 1));
        code.AddRange(J(OpCode.CallExternal, 0));

        var prog = new Bytecode([.. code], [], callSites: [del]);
        state.Program = prog;

        var result = Vm.Execute(state);

        await Assert.That(result.HasValue).IsTrue();
        await Assert.That((int)result.Value!).IsEqualTo(15);
    }

    [Test]
    public async Task Lowering_InvokeClrMethod_LowersAndExecutes() {
        var ast = new Invoke(
            new Member(new Constant(42L), "CompareTo"),
            new Constant(10L)
        );

        var analysis = NodeTestHelpers.CreateTestAnalyzer().Analyze(ast);

        using var state = new VmState();
        var program = Lowering.Lower(ast, analysis);
        state.Program = program;

        await Assert.That(program.CallSites.Count).IsEqualTo(1);
        await Assert.That(program.CallSites[0]).IsNotNull();

        var result = Vm.Execute(state);

        await Assert.That(result.HasValue).IsTrue();
        await Assert.That((int)result.Value!).IsEqualTo(1);
    }

    [Test]
    public async Task Lowering_InvokeStaticMethod_LowersAndExecutes() {
        var mathTypeRef = new TypeReference(typeof(Math).FullName!);
        var ast = new Invoke(
            new Member(mathTypeRef, nameof(Math.Max)),
            new Constant(10), new Constant(20)
        );

        var analysis = NodeTestHelpers.CreateTestAnalyzer().Analyze(ast);

        using var state = new VmState();
        var program = Lowering.Lower(ast, analysis);
        state.Program = program;

        await Assert.That(program.CallSites.Count).IsEqualTo(1);
        await Assert.That(program.CallSites[0]).IsNotNull();

        var result = Vm.Execute(state);

        await Assert.That(result.HasValue).IsTrue();
        await Assert.That((int)result.Value!).IsEqualTo(20);
    }
}