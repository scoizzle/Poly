using System.IO;
using System.Linq.Expressions;
using System.Reflection;

using Poly.Interpretation;
using Poly.Interpretation.Analysis;
using Poly.Interpretation.Analysis.ConstantFolding;
using Poly.Interpretation.Analysis.ControlFlow;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.LinqExpressions;
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
            .. J(OpCode.Jump, 27),          // jump past chunk 2 (offset 27 = 3 * 9)
            .. J(OpCode.Push, 99),          // chunk 2: skipped
            .. J(OpCode.Push, 2),           // chunk 3 (offset 27)
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
            .. J(OpCode.JumpIfFalse, 27),   // jump past chunk 2 (offset 27 = 3 * 9)
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

    [Test]
    public async Task Vm_LambdaInvoke_NoParameters() {
        // (() => 42)() = 42
        var lambda = new Lambda([], new Constant(42));
        var invoke = new Invoke(lambda);

        var analysis = new AnalyzerBuilder()
            .UseTypeResolver()
            .UseMemberResolver()
            .Build()
            .Analyze(invoke);

        var program = Lowering.Lower(invoke, analysis);
        using var state = new VmState { Program = program };
        var result = Vm.Execute(state);

        await Assert.That(state.IsComplete).IsTrue();
        await Assert.That(result.Value).IsEqualTo(42L);
    }

    [Test]
    public async Task Vm_LambdaInvoke_WithParameter() {
        // (x => x + 1)(5) = 6
        var param = new Parameter("x", TypeReference.To<int>());
        var lambda = new Lambda([param], new Add(param, new Constant(1)));
        var invoke = new Invoke(lambda, new Constant(5));

        var analysis = new AnalyzerBuilder()
            .UseTypeResolver()
            .UseMemberResolver()
            .Build()
            .Analyze(invoke);

        var program = Lowering.Lower(invoke, analysis);
        using var state = new VmState { Program = program };
        var result = Vm.Execute(state);

        await Assert.That(state.IsComplete).IsTrue();
        await Assert.That(result.Value).IsEqualTo(6L);
    }

    [Test]
    public async Task Vm_LambdaInvoke_MultipleParameters() {
        // ((x, y) => x + y)(3, 4) = 7
        var x = new Parameter("x", TypeReference.To<int>());
        var y = new Parameter("y", TypeReference.To<int>());
        var lambda = new Lambda([x, y], new Add(x, y));
        var invoke = new Invoke(lambda, new Constant(3), new Constant(4));

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
    public async Task Vm_LambdaInvoke_MultipleCalls() {
        // (x => x * 2)(5) = 10 and (x => x * 2)(3) = 6
        var param = new Parameter("x", TypeReference.To<int>());
        var lambda = new Lambda([param], new Multiply(param, new Constant(2)));
        var invoke5 = new Invoke(lambda, new Constant(5));
        var invoke3 = new Invoke(lambda, new Constant(3));

        var analysis5 = new AnalyzerBuilder()
            .UseTypeResolver()
            .UseMemberResolver()
            .Build()
            .Analyze(invoke5);

        var program5 = Lowering.Lower(invoke5, analysis5);
        using var state5 = new VmState { Program = program5 };
        var result5 = Vm.Execute(state5);

        await Assert.That(state5.IsComplete).IsTrue();
        await Assert.That(result5.Value).IsEqualTo(10L);

        var analysis3 = new AnalyzerBuilder()
            .UseTypeResolver()
            .UseMemberResolver()
            .Build()
            .Analyze(invoke3);

        var program3 = Lowering.Lower(invoke3, analysis3);
        using var state3 = new VmState { Program = program3 };
        var result3 = Vm.Execute(state3);

        await Assert.That(state3.IsComplete).IsTrue();
        await Assert.That(result3.Value).IsEqualTo(6L);
    }

    [Test]
    public async Task Vm_ManualBytecode_SimpleCall() {
        // Simplest Call/Return: function pushes 42
        // Layout: Push -1(dummy), Push 1(argCount), Call 0, Return(main)
        //         Push 42(func body), Return(func ret)
        var c = new List<byte>();
        c.AddRange(J(OpCode.Push, -1L));
        c.AddRange(J(OpCode.Push, 1L));
        c.AddRange(J(OpCode.Call, 0));
        c.Add((byte)OpCode.Return);
        c.AddRange(J(OpCode.Push, 42L));
        c.Add((byte)OpCode.Return);

        var program = new Bytecode([.. c], [], [new(28, 1, 1, 0)]);
        using var state = new VmState { Program = program };
        var result = Vm.Execute(state);
        await Assert.That(state.IsComplete).IsTrue();
        await Assert.That(result.Value).IsEqualTo(42L);
    }

    [Test]
    public async Task Vm_ManualBytecode_LambdaCall() {
        // Manually construct: same bytecode as (x => x + 1)(5)
        var c = new List<byte>();
        c.AddRange(J(OpCode.Push, -1L));
        c.AddRange(J(OpCode.Push, 5L));
        c.AddRange(J(OpCode.Push, 2L));
        c.AddRange(J(OpCode.Call, 0));
        c.Add((byte)OpCode.Return);
        c.AddRange(J(OpCode.LoadArg, 1));
        c.AddRange(J(OpCode.Push, 1L));
        c.Add((byte)OpCode.Add);
        c.Add((byte)OpCode.Return);

        var program = new Bytecode([.. c], [], [new(37, 2, 1, 0)]);
        using var state = new VmState { Program = program };
        var result = Vm.Execute(state);

        await Assert.That(state.IsComplete).IsTrue();
        await Assert.That(result.Value).IsEqualTo(6L);
    }

    [Test]
    public async Task Vm_Lambda_WhileLoop_Basic() {
        // While(false) { } — never executes, returns null (void)
        var body = new Block([new Constant(42)]);
        var wl = new WhileLoop(new Constant(0), body);
        var lambda = new Lambda([], wl);
        var invoke = new Invoke(lambda);

        var analysis = new AnalyzerBuilder()
            .UseTypeResolver()
            .UseMemberResolver()
            .Build()
            .Analyze(invoke);

        var program = Lowering.Lower(invoke, analysis);
        using var state = new VmState { Program = program };
        var result = Vm.Execute(state);
        await Assert.That(state.IsComplete).IsTrue();
    }

    [Test]
    public async Task Vm_Lambda_WithVariables() {
        var x = new Variable("x"); var y = new Variable("y");
        var body = new Block(
            [new Assignment(x, new Constant(5)),
             new Assignment(y, new Constant(3)),
             new Add(x, y)],
            [x, y]
        );
        var lambda = new Lambda([], body);
        var invoke = new Invoke(lambda);

        var analysis = new AnalyzerBuilder()
            .UseTypeResolver().UseMemberResolver().UseConstantFolding()
            .UseSideEffectAnalysis().UseThisReferenceContext()
            .UseControlFlowAnalysis().UseVariableScopeValidator()
            .Build().Analyze(invoke);

        var program = Lowering.Lower(invoke, analysis);
        using var state = new VmState { Program = program };
        var result = Vm.Execute(state);
        await Assert.That(state.IsComplete).IsTrue();
        await Assert.That(result.Value).IsEqualTo(8L);
    }

    [Test]
    public async Task Vm_Lambda_WhileLoop_IncLocal() {
        var iVar = new Variable("i");
        var lambda = new Lambda([], new Block(
            [new Assignment(iVar, new Constant(1)),
             new WhileLoop(new LessThanOrEqual(iVar, new Constant(3)),
                 new Block([new Assignment(iVar, new Add(iVar, new Constant(1)))])),
             iVar],
            [iVar]
        ));
        var invoke = new Invoke(lambda);
        var analysis = new AnalyzerBuilder()
            .UseTypeResolver().UseMemberResolver().UseConstantFolding()
            .UseSideEffectAnalysis().UseThisReferenceContext()
            .UseControlFlowAnalysis().UseVariableScopeValidator()
            .Build().Analyze(invoke);
        var program = Lowering.Lower(invoke, analysis);
        using var state = new VmState { Program = program };
        var result = Vm.Execute(state);
        await Assert.That(state.IsComplete).IsTrue();
        await Assert.That(result.Value).IsEqualTo(4L);
    }

    [Test]
    public async Task Vm_Lambda_AssignAdd() {
        // sum = sum + i (no loop, no IncLocal)
        var sumVar = new Variable("sum"); var iVar = new Variable("i");
        var lambda = new Lambda([], new Block(
            [new Assignment(sumVar, new Constant(5)),
             new Assignment(iVar, new Constant(3)),
             new Assignment(sumVar, new Add(sumVar, iVar)),
             sumVar],
            [sumVar, iVar]
        ));
        var invoke = new Invoke(lambda);
        var analysis = new AnalyzerBuilder()
            .UseTypeResolver().UseMemberResolver().UseConstantFolding()
            .UseSideEffectAnalysis().UseThisReferenceContext()
            .UseControlFlowAnalysis().UseVariableScopeValidator()
            .Build().Analyze(invoke);
        var program = Lowering.Lower(invoke, analysis);
        using var state = new VmState { Program = program };
        var result = Vm.Execute(state);
        await Assert.That(state.IsComplete).IsTrue();
        await Assert.That(result.Value).IsEqualTo(8L);
    }

    [Test]
    public async Task Vm_Lambda_WhileLoop_Sum() {
        var sumVar = new Variable("sum"); var iVar = new Variable("i");
        var lambda = new Lambda([], new Block(
            [new Assignment(sumVar, new Constant(0)),
             new Assignment(iVar, new Constant(1)),
             new WhileLoop(new LessThanOrEqual(iVar, new Constant(10)),
                 new Block([
                     new Assignment(sumVar, new Add(sumVar, iVar)),
                     new Assignment(iVar, new Add(iVar, new Constant(1)))
                 ])),
             sumVar],
            [sumVar, iVar]
        ));
        var invoke = new Invoke(lambda);
        var analysis = new AnalyzerBuilder()
            .UseTypeResolver().UseMemberResolver().UseConstantFolding()
            .UseSideEffectAnalysis().UseThisReferenceContext()
            .UseControlFlowAnalysis().UseVariableScopeValidator()
            .Build().Analyze(invoke);
        var program = Lowering.Lower(invoke, analysis);
        using var state = new VmState { Program = program };
        var result = Vm.Execute(state);
        await Assert.That(state.IsComplete).IsTrue();
        await Assert.That(result.Value).IsEqualTo(55L);
    }

    [Test]
    public async Task Vm_Lambda_WhileLoop_Sum_Jit() {
        // Same as Sum but 15 iterations to trigger loop body JIT (threshold=10)
        // sum(1..15) = 120
        var sumVar = new Variable("sum"); var iVar = new Variable("i");
        var lambda = new Lambda([], new Block(
            [new Assignment(sumVar, new Constant(0)),
             new Assignment(iVar, new Constant(1)),
             new WhileLoop(new LessThanOrEqual(iVar, new Constant(15)),
                 new Block([
                     new Assignment(sumVar, new Add(sumVar, iVar)),
                     new Assignment(iVar, new Add(iVar, new Constant(1)))
                 ])),
             sumVar],
            [sumVar, iVar]
        ));
        var invoke = new Invoke(lambda);
        var analysis = new AnalyzerBuilder()
            .UseTypeResolver().UseMemberResolver().UseConstantFolding()
            .UseSideEffectAnalysis().UseThisReferenceContext()
            .UseControlFlowAnalysis().UseVariableScopeValidator()
            .Build().Analyze(invoke);
        var program = Lowering.Lower(invoke, analysis);
        using var state = new VmState { Program = program };
        var result = Vm.Execute(state);
        await Assert.That(state.IsComplete).IsTrue();
        // If result != 120, the loop body JIT doesn't write mutated variables back to VM stack
        await Assert.That(result.Value).IsEqualTo(120L);
    }

    [Test]
    public async Task Vm_Lambda_DoWhileLoop_Sum() {
        // sum = 0; i = 1; do { sum += i; i++; } while (i <= 10); sum → 55
        var sumVar = new Variable("sum"); var iVar = new Variable("i");
        var lambda = new Lambda([], new Block(
            [new Assignment(sumVar, new Constant(0)),
             new Assignment(iVar, new Constant(1)),
             new DoWhileLoop(
                 new Block([
                     new Assignment(sumVar, new Add(sumVar, iVar)),
                     new Assignment(iVar, new Add(iVar, new Constant(1)))
                 ]),
                 new LessThanOrEqual(iVar, new Constant(10))),
             sumVar],
            [sumVar, iVar]
        ));
        var invoke = new Invoke(lambda);
        var analysis = new AnalyzerBuilder()
            .UseTypeResolver().UseMemberResolver().UseConstantFolding()
            .UseSideEffectAnalysis().UseThisReferenceContext()
            .UseControlFlowAnalysis().UseVariableScopeValidator()
            .Build().Analyze(invoke);
        var program = Lowering.Lower(invoke, analysis);
        using var state = new VmState { Program = program };
        var result = Vm.Execute(state);
        await Assert.That(state.IsComplete).IsTrue();
        await Assert.That(result.Value).IsEqualTo(55L);
    }

    [Test]
    public async Task Vm_Lambda_ForLoop_Sum() {
        // sum = 0; for (int i = 1; i <= 10; i++) sum += i; sum → 55
        var sumVar = new Variable("sum"); var iVar = new Variable("i");
        var lambda = new Lambda([], new Block(
            [new Assignment(sumVar, new Constant(0)),
             new ForLoop(
                 new Assignment(iVar, new Constant(1)),    // initializer
                 new LessThanOrEqual(iVar, new Constant(10)), // condition
                 new Assignment(iVar, new Add(iVar, new Constant(1))), // increment
                 new Assignment(sumVar, new Add(sumVar, iVar)) // body
             ),
             sumVar],
            [sumVar, iVar]
        ));
        var invoke = new Invoke(lambda);
        var analysis = new AnalyzerBuilder()
            .UseTypeResolver().UseMemberResolver().UseConstantFolding()
            .UseSideEffectAnalysis().UseThisReferenceContext()
            .UseControlFlowAnalysis().UseVariableScopeValidator()
            .Build().Analyze(invoke);
        var program = Lowering.Lower(invoke, analysis);
        using var state = new VmState { Program = program };
        var result = Vm.Execute(state);
        await Assert.That(state.IsComplete).IsTrue();
        await Assert.That(result.Value).IsEqualTo(55L);
    }

    [Test]
    public async Task Vm_Lambda_ForLoop_IncLocal() {
        // for (int i = 1; i <= 3; i++) { }; i → 4
        var iVar = new Variable("i");
        var lambda = new Lambda([], new Block(
            [new ForLoop(
                 new Assignment(iVar, new Constant(1)),
                 new LessThanOrEqual(iVar, new Constant(3)),
                 new Assignment(iVar, new Add(iVar, new Constant(1))),
                 new Constant(0) // body: no-op
             ),
             iVar],
            [iVar]
        ));
        var invoke = new Invoke(lambda);
        var analysis = new AnalyzerBuilder()
            .UseTypeResolver().UseMemberResolver().UseConstantFolding()
            .UseSideEffectAnalysis().UseThisReferenceContext()
            .UseControlFlowAnalysis().UseVariableScopeValidator()
            .Build().Analyze(invoke);
        var program = Lowering.Lower(invoke, analysis);
        using var state = new VmState { Program = program };
        var result = Vm.Execute(state);
        await Assert.That(state.IsComplete).IsTrue();
        await Assert.That(result.Value).IsEqualTo(4L);
    }

    [Test]
    public async Task Vm_Lambda_NestedWhile_Product() {
        // sum = 0; i = 1; while (i <= 3) { j = 1; while (j <= 3) { sum += i * j; j++; } i++; }
        // = (1*1+1*2+1*3)+(2*1+2*2+2*3)+(3*1+3*2+3*3) = 6+12+18 = 36
        var sumVar = new Variable("sum"); var iVar = new Variable("i"); var jVar = new Variable("j");
        var lambda = new Lambda([], new Block(
            [new Assignment(sumVar, new Constant(0)),
             new Assignment(iVar, new Constant(1)),
             new WhileLoop(new LessThanOrEqual(iVar, new Constant(3)),
                 new Block([
                     new Assignment(jVar, new Constant(1)),
                     new WhileLoop(new LessThanOrEqual(jVar, new Constant(3)),
                         new Block([
                             new Assignment(sumVar, new Add(sumVar, new Multiply(iVar, jVar))),
                             new Assignment(jVar, new Add(jVar, new Constant(1)))
                         ])),
                     new Assignment(iVar, new Add(iVar, new Constant(1)))
                 ])),
             sumVar],
            [sumVar, iVar, jVar]
        ));
        var invoke = new Invoke(lambda);
        var analysis = new AnalyzerBuilder()
            .UseTypeResolver().UseMemberResolver().UseConstantFolding()
            .UseSideEffectAnalysis().UseThisReferenceContext()
            .UseControlFlowAnalysis().UseVariableScopeValidator()
            .Build().Analyze(invoke);
        var program = Lowering.Lower(invoke, analysis);
        using var state = new VmState { Program = program };
        var result = Vm.Execute(state);
        await Assert.That(state.IsComplete).IsTrue();
        await Assert.That(result.Value).IsEqualTo(36L);
    }

    [Test]
    public async Task Vm_Lambda_LoopBodyOnly() {
        // While loop as the ONLY expression in the lambda body — no preceding/following nodes
        // i = 1; while (i <= 1) { i++; }; i (only the loop and the result variable)
        var iVar = new Variable("i");
        var lambda = new Lambda([], new Block(
            [new Assignment(iVar, new Constant(1)),
             new WhileLoop(new LessThanOrEqual(iVar, new Constant(1)),
                 new Block([new Assignment(iVar, new Add(iVar, new Constant(1)))])),
             iVar],
            [iVar]
        ));
        var invoke = new Invoke(lambda);
        var analysis = new AnalyzerBuilder()
            .UseTypeResolver().UseMemberResolver().UseConstantFolding()
            .UseSideEffectAnalysis().UseThisReferenceContext()
            .UseControlFlowAnalysis().UseVariableScopeValidator()
            .Build().Analyze(invoke);
        var program = Lowering.Lower(invoke, analysis);
        using var state = new VmState { Program = program };
        var result = Vm.Execute(state);
        await Assert.That(state.IsComplete).IsTrue();
        await Assert.That(result.Value).IsEqualTo(2L);
    }

    [Test]
    public async Task Vm_Lambda_TwoLoops_Sum() {
        // Two consecutive while loops: first sums 1..5, second sums 6..10, total = 15 + 40 = 55
        var sumVar = new Variable("sum"); var iVar = new Variable("i");
        var lambda = new Lambda([], new Block(
            [new Assignment(sumVar, new Constant(0)),
             new Assignment(iVar, new Constant(1)),
             new WhileLoop(new LessThanOrEqual(iVar, new Constant(5)),
                 new Block([
                     new Assignment(sumVar, new Add(sumVar, iVar)),
                     new Assignment(iVar, new Add(iVar, new Constant(1)))
                 ])),
             new WhileLoop(new LessThanOrEqual(iVar, new Constant(10)),
                 new Block([
                     new Assignment(sumVar, new Add(sumVar, iVar)),
                     new Assignment(iVar, new Add(iVar, new Constant(1)))
                 ])),
             sumVar],
            [sumVar, iVar]
        ));
        var invoke = new Invoke(lambda);
        var analysis = new AnalyzerBuilder()
            .UseTypeResolver().UseMemberResolver().UseConstantFolding()
            .UseSideEffectAnalysis().UseThisReferenceContext()
            .UseControlFlowAnalysis().UseVariableScopeValidator()
            .Build().Analyze(invoke);
        var program = Lowering.Lower(invoke, analysis);
        using var state = new VmState { Program = program };
        var result = Vm.Execute(state);
        await Assert.That(state.IsComplete).IsTrue();
        await Assert.That(result.Value).IsEqualTo(55L);
    }

    [Test]
    public async Task Vm_Lambda_WhileLoop_AndCondition() {
        // while (i <= 10 AND sum < 30) { sum += i; i++; }
        // Sum until either i > 10 or sum >= 30: i=1..7 → sum=28, i=8 → sum=36≥30 stops
        var sumVar = new Variable("sum"); var iVar = new Variable("i");
        var lambda = new Lambda([], new Block(
            [new Assignment(sumVar, new Constant(0)),
             new Assignment(iVar, new Constant(1)),
             new WhileLoop(
                 new And(new LessThanOrEqual(iVar, new Constant(10)),
                         new LessThan(sumVar, new Constant(30))),
                 new Block([
                     new Assignment(sumVar, new Add(sumVar, iVar)),
                     new Assignment(iVar, new Add(iVar, new Constant(1)))
                 ])),
             sumVar],
            [sumVar, iVar]
        ));
        var invoke = new Invoke(lambda);
        var analysis = new AnalyzerBuilder()
            .UseTypeResolver().UseMemberResolver().UseConstantFolding()
            .UseSideEffectAnalysis().UseThisReferenceContext()
            .UseControlFlowAnalysis().UseVariableScopeValidator()
            .Build().Analyze(invoke);
        var program = Lowering.Lower(invoke, analysis);
        using var state = new VmState { Program = program };
        var result = Vm.Execute(state);
        await Assert.That(state.IsComplete).IsTrue();
        await Assert.That(result.Value).IsEqualTo(36L); // sum(1..8) = 36 (i=8 enters, then 36<30 stops)
    }

    [Test]
    public async Task Vm_ManualBytecode_SimplePush() {
        var c = new List<byte>();
        c.AddRange(J(OpCode.Push, 42L));
        c.Add((byte)OpCode.Return);
        var program = new Bytecode([.. c], [], null);
        using var state = new VmState { Program = program };
        var result = Vm.Execute(state);
        await Assert.That(state.IsComplete).IsTrue();
        await Assert.That(result.Value).IsEqualTo(42L);
    }

    [Test]
    public async Task Vm_Dump_Bytecode() {
        var outDir = "/tmp/poly_vm_dump";
        Directory.CreateDirectory(outDir);

        // Helper to dump bytecode
        void DumpBytecode(Bytecode prog, string label, string path) {
            using var w = new StreamWriter(path);
            w.WriteLine($"=== {label} ===");
            w.WriteLine($"Code: {prog.Code.Length} bytes  (max stack: {prog.MaxStackDepth})");
            w.WriteLine($"Functions: {prog.Functions.Count}");
            w.WriteLine($"Loop bodies: {prog.LoopBodies.Count}");
            w.WriteLine($"Constants: {prog.Constants.Count}");
            w.WriteLine($"Call sites: {prog.CallSites.Count}");
            for (int si = 0; si < prog.CallSites.Count; si++) {
                var desc = si < prog.CallSiteTargets.Count ? prog.CallSiteTargets[si] : "";
                w.WriteLine($"  Site[{si}]: {desc}");
            }

            for (int fi = 0; fi < prog.Functions.Count; fi++) {
                var fn = prog.Functions[fi];
                w.WriteLine($"\nFunc[{fi}]: PC=0x{fn.PC:X4} ArgBytes={fn.ArgBytes} RetBytes={fn.RetBytes} LocalCount={fn.LocalCount} SourceNode={fn.SourceNode?.GetType().Name}");
            }

            for (int li = 0; li < prog.LoopBodies.Count; li++) {
                var lb = prog.LoopBodies[li];
                w.WriteLine($"\nLoopBody[{li}]: BodyPC=0x{lb.BodyPC:X4} BodyLen={lb.BodyLength} ContPC=0x{lb.ContPC:X4} ContinuePC=0x{lb.ContinuePC:X4} EndPC=0x{lb.EndPC:X4}");
            }

            w.WriteLine("\nBytecode dump:");
            int pos = 0;
            while (pos < prog.Code.Length) {
                byte raw = prog.Code[pos];
                int size = (raw & OpCodeEncoding.SizeBit) != 0 ? 9 : 1;
                var op = (OpCode)(raw & OpCodeEncoding.OpcodeMask);
                w.Write($"  0x{pos:X4}: ");
                for (int i = 0; i < size && pos + i < prog.Code.Length; i++)
                    w.Write($"{prog.Code[pos + i]:X2} ");
                w.Write(new string(' ', Math.Max(0, 24 - size * 3)));
                w.Write($"// {op,-13}");
                long operand = size == 9 ? BitConverter.ToInt64(prog.Code, pos + 1) : 0;
                if (size == 9) {
                    if (op is OpCode.Jump or OpCode.JumpIfFalse)
                        w.Write($"-> 0x{operand:X4}");
                    else if (op is OpCode.IncLocal)
                        w.Write($"slot={(int)(operand >> 32)} delta={(int)operand}");
                    else if (op is OpCode.LoadLocal or OpCode.StoreLocal or OpCode.LoadArg or OpCode.StoreArg)
                        w.Write($"slot={operand}");
                    else if (op is OpCode.Call)
                        w.Write($"func={operand}");
                    else if (op is OpCode.CallExternal) {
                        var si = (int)operand;
                        w.Write($"site={si}");
                        if (si < prog.CallSiteTargets.Count)
                            w.Write($" {prog.CallSiteTargets[si]}");
                    }
                    else if (op is OpCode.Push)
                        w.Write($"0x{operand:X}");
                    else
                        w.Write($"0x{operand:X}");
                }
                w.WriteLine();
                pos += size;
            }
        }

        // 1. While loop sum
        var sumVar = new Variable("sum"); var iVar = new Variable("i");
        var whileSum = new Invoke(new Lambda([], new Block(
            [new Assignment(sumVar, new Constant(0)),
             new Assignment(iVar, new Constant(1)),
             new WhileLoop(new LessThanOrEqual(iVar, new Constant(10)),
                 new Block([
                     new Assignment(sumVar, new Add(sumVar, iVar)),
                     new Assignment(iVar, new Add(iVar, new Constant(1)))
                 ])),
             sumVar],
            [sumVar, iVar]
        )));
        var wa = new AnalyzerBuilder()
            .UseTypeResolver().UseMemberResolver().UseConstantFolding()
            .UseSideEffectAnalysis().UseThisReferenceContext()
            .UseControlFlowAnalysis().UseVariableScopeValidator()
            .Build().Analyze(whileSum);
        DumpBytecode(Lowering.Lower(whileSum, wa), "While Loop Sum", Path.Combine(outDir, "while_loop_sum.txt"));

        // 2. CLR call chain
        var maxMethod = new Member(new TypeReference(typeof(Math).FullName!), nameof(Math.Max));
        Node chain = new Constant(1);
        for (int i = 2; i <= 10; i++)
            chain = new Invoke(maxMethod, chain, new Constant(i));
        var ca = new AnalyzerBuilder()
            .UseTypeResolver().UseMemberResolver().UseConstantFolding()
            .UseSideEffectAnalysis().UseThisReferenceContext()
            .UseControlFlowAnalysis().Build().Analyze(chain);
        DumpBytecode(Lowering.Lower(chain, ca), "CLR Call Chain (10)", Path.Combine(outDir, "clr_chain_10.txt"));

        // 3. Deep sum (balanced tree)
        Node BuildDeep(int n) {
            var vals = new int[n];
            for (int i = 0; i < n; i++) vals[i] = i + 1;
            return BuildBal(vals, 0, n - 1);
            static Node BuildBal(int[] v, int s, int e) =>
                s == e ? new Constant(v[s]) : new Add(BuildBal(v, s, (s + e) / 2), BuildBal(v, (s + e) / 2 + 1, e));
        }
        var deep = BuildDeep(20);
        var da = new AnalyzerBuilder()
            .UseTypeResolver().UseMemberResolver().UseConstantFolding()
            .UseSideEffectAnalysis().UseThisReferenceContext()
            .UseControlFlowAnalysis().Build().Analyze(deep);
        DumpBytecode(Lowering.Lower(deep, da), "Deep Sum (20)", Path.Combine(outDir, "deep_sum_20.txt"));

        // 5. Expression tree: CallSiteCompiler wrapper for Math.Max
        using var csw = new StreamWriter(Path.Combine(outDir, "callexpr_math_max.txt"));
        csw.WriteLine("=== CallSiteCompiler Generated Expression: Math.Max wrapper ===");
        csw.WriteLine("(Expression tree is built dynamically inside CallSiteCompiler.Compile)");
        csw.WriteLine("The wrapper reads args from VmState.Stack, calls Math.Max, pushes result.\n");
        var maxMethodInfo = typeof(Math).GetMethod(nameof(Math.Max), [typeof(int), typeof(int)])!;
        var maxDel = CallSiteCompiler.Compile(maxMethodInfo, isStatic: true);
        csw.WriteLine($"Compiled delegate: {maxDel.Method}");
        csw.WriteLine($"Target closure fields:");
        if (maxDel.Target is not null)
            foreach (var f in maxDel.Target.GetType().GetFields(BindingFlags.NonPublic | BindingFlags.Instance))
                csw.WriteLine($"  {f.Name}: {f.GetValue(maxDel.Target)?.GetType().Name}");

        // 6. Expression tree from LinqExpressionGenerator for x => x + 1
        using var jw = new StreamWriter(Path.Combine(outDir, "jitexpr_add_one.txt"));
        jw.WriteLine("=== LinqExpressionGenerator Expression: x => x + 1 ===");
        var jitX = new Parameter("x", TypeReference.To<int>());
        var jitExpr = new Add(jitX, new Constant(1));
        var jitLambda = new Lambda([jitX], jitExpr);
        var jitAnalysis2 = new AnalyzerBuilder()
            .UseTypeResolver().UseMemberResolver().UseConstantFolding()
            .UseSideEffectAnalysis().UseThisReferenceContext()
            .UseControlFlowAnalysis().Build()
            .Analyze(jitLambda);
        var gen = new LinqExpressionGenerator(jitAnalysis2);
        var compileResult = gen.Compile(jitLambda.Body);
        jw.WriteLine($"Expression: {compileResult.Expression}");
        jw.WriteLine($"Type: {compileResult.Expression.Type}");
        jw.WriteLine($"Parameters ({compileResult.Parameters.Count}):");
        foreach (var p in compileResult.Parameters)
            jw.WriteLine($"  {p.Name}: {p.Type}");
        jw.WriteLine($"\nToString(): {compileResult.Expression}");

        // 7. Expression tree for while loop body (using Parameter for typed vars)
        using var lbw = new StreamWriter(Path.Combine(outDir, "jitexpr_loop_body.txt"));
        lbw.WriteLine("=== LinqExpressionGenerator Expression: while loop body ===");
        var lbSp = new Parameter("s", TypeReference.To<int>());
        var lbIp = new Parameter("i", TypeReference.To<int>());
        var lbBody2 = new Block([
            new Assignment(lbSp, new Add(lbSp, lbIp)),
            new Assignment(lbIp, new Add(lbIp, new Constant(1)))
        ]);
        var lbLambda3 = new Lambda([lbSp, lbIp], lbBody2);
        var lbAnalysis3 = new AnalyzerBuilder()
            .UseTypeResolver().UseMemberResolver().UseConstantFolding()
            .UseSideEffectAnalysis().UseThisReferenceContext()
            .UseControlFlowAnalysis().Build().Analyze(lbLambda3);
        var lbGen2 = new LinqExpressionGenerator(lbAnalysis3);
        var lbResult2 = lbGen2.Compile(lbBody2);
        lbw.WriteLine($"Expression: {lbResult2.Expression}");
        lbw.WriteLine($"Type: {lbResult2.Expression.Type}");
        lbw.WriteLine($"Parameters ({lbResult2.Parameters.Count}):");
        foreach (var p in lbResult2.Parameters)
            lbw.WriteLine($"  {p.Name}: {p.Type}");
        lbw.WriteLine($"\nToString(): {lbResult2.Expression}");
        if (lbResult2.Expression is BlockExpression block) {
            lbw.WriteLine($"\nBlock expressions ({block.Expressions.Count}):");
            for (int i = 0; i < block.Expressions.Count; i++)
                lbw.WriteLine($"  [{i}] {block.Expressions[i]}");
        }

        // 8. Expression tree for deep sum chain
        using var dsw = new StreamWriter(Path.Combine(outDir, "jitexpr_deep_sum.txt"));
        dsw.WriteLine("=== LinqExpressionGenerator Expression: sum(1..20) ===");
        Node BuildDeep8(int n) {
            var vals = new int[n];
            for (int i = 0; i < n; i++) vals[i] = i + 1;
            return BuildBal(vals, 0, n - 1);
            static Node BuildBal(int[] v, int s, int e) =>
                s == e ? new Constant(v[s]) : new Add(BuildBal(v, s, (s + e) / 2), BuildBal(v, (s + e) / 2 + 1, e));
        }
        var deep8 = BuildDeep8(20);
        var deepAnalysis2 = new AnalyzerBuilder()
            .UseTypeResolver().UseMemberResolver().UseConstantFolding()
            .UseSideEffectAnalysis().UseThisReferenceContext()
            .UseControlFlowAnalysis().Build().Analyze(deep8);
        var deepGen = new LinqExpressionGenerator(deepAnalysis2);
        var deepResult = deepGen.Compile(deep8);
        dsw.WriteLine($"Expression: {deepResult.Expression}");
        dsw.WriteLine($"Type: {deepResult.Expression.Type}");
        dsw.WriteLine($"\nToString(): {deepResult.Expression}");

        await Assert.That(File.Exists(Path.Combine(outDir, "while_loop_sum.txt"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(outDir, "clr_chain_10.txt"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(outDir, "deep_sum_20.txt"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(outDir, "callexpr_math_max.txt"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(outDir, "jitexpr_add_one.txt"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(outDir, "jitexpr_loop_body.txt"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(outDir, "jitexpr_deep_sum.txt"))).IsTrue();
    }

}