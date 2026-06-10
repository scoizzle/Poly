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
}