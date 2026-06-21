using Poly.Interpretation.Vm;
using Poly.Interpretation.Vm.Instructions;

namespace Poly.Tests.Interpretation;

/// <summary>End-to-end tests for the register-based compiler pipeline.</summary>
public class NewCompilerBasicTests {
    static object? Run(Instruction[] instructions, int maxActiveLocalDepth = 32) {
        var loweringResult = new LoweringResult([.. instructions], MaxActiveLocalsDepth: 1);
        var program = ProgramCompiler.Compile(loweringResult, maxActiveLocalDepth: maxActiveLocalDepth, mode: CompilationMode.Normal);
        var state = new VmState(program) { MaxLoopIterations = 100_000_000 };

        program.Delegate(state);
        var result = state.Stack.StackPointer > 0
            ? (object?)state.Stack.RawSlots[0]
            : null;
        return result;
    }

    [Test]
    public async Task AddMul_Prog2() {
        var result = Run([
            new LoadConst(3),
            new LoadConst(4),
            new BinOp(BinOpKind.Add),
            new LoadConst(2),
            new BinOp(BinOpKind.Mul),
            new ReturnOp(),
        ]);
        await Assert.That(result).IsEqualTo(14L);
    }

    [Test]
    public async Task LoadConst_Prog2() {
        var result = Run([
            new LoadConst(42),
            new ReturnOp(),
        ]);
        await Assert.That(result).IsEqualTo(42L);
    }

    [Test]
    public async Task Sub_Prog2() {
        var result = Run([
            new LoadConst(10),
            new LoadConst(3),
            new BinOp(BinOpKind.Sub),
            new ReturnOp(),
        ]);
        await Assert.That(result).IsEqualTo(7L);
    }

    [Test]
    public async Task Compare_Prog2() {
        var result = Run([
            new LoadConst(5),
            new LoadConst(5),
            new BinOp(BinOpKind.Eq),
            new ReturnOp(),
        ]);
        await Assert.That(result).IsEqualTo(1L);
    }

    [Test]
    public async Task Branch_Prog2() {
        var result = Run([
            new LoadConst(0),
            new BranchIfFalse(4) { ConsumedFromPcs = [0] },
            new LoadConst(1),
            new Jump(5),
            new LoadConst(2),
            new ReturnOp() { ConsumedFromPcs = [2], PhiSourcePcs = [1], PhiAltPcs = [4] },
        ]);
        await Assert.That(result).IsEqualTo(2L);
    }

    [Test]
    public async Task BranchTrue_Prog2() {
        var result = Run([
            new LoadConst(1),
            new BranchIfFalse(4) { ConsumedFromPcs = [0] },
            new LoadConst(10),
            new Jump(5),
            new LoadConst(20),
            new ReturnOp() { ConsumedFromPcs = [2], PhiSourcePcs = [1], PhiAltPcs = [4] },
        ]);
        await Assert.That(result).IsEqualTo(10L);
    }

    [Test]
    public async Task ChainOps_Prog2() {
        var result = Run([
            new LoadConst(10),
            new LoadConst(2),
            new BinOp(BinOpKind.Add),
            new LoadConst(6),
            new LoadConst(1),
            new BinOp(BinOpKind.Sub),
            new BinOp(BinOpKind.Mul),
            new ReturnOp(),
        ]);
        await Assert.That(result).IsEqualTo(60L);
    }

    [Test]
    public async Task RingSpill_ExceedsCap_DropsToStack() {
        // Ring depth hits 3 (LoadConst × 3), but cap is 2.
        // The third value spills to state.Registers[0].
        var result = Run([
            new LoadConst(1),    // _r0 = 1, ring depth 1
            new LoadConst(2),    // _r1 = 2, ring depth 2
            new LoadConst(3),    // spills to regs[0], ring depth 3
            new BinOp(BinOpKind.Add), // pop 3,2 → 5 → _r1 = 5, ring depth 2
            new BinOp(BinOpKind.Add), // pop 5,1 → 6 → _r0 = 6, ring depth 1
            new ReturnOp(),
        ], maxActiveLocalDepth: 2);
        await Assert.That(result).IsEqualTo(6L);
    }

    [Test]
    public async Task RingSpill_LargerCap_UsesRegisters() {
        // Same µops, cap=4 (no spill needed) — should still produce correct result.
        var result = Run([
            new LoadConst(1),
            new LoadConst(2),
            new LoadConst(3),
            new BinOp(BinOpKind.Add),
            new BinOp(BinOpKind.Add),
            new ReturnOp(),
        ], maxActiveLocalDepth: 4);
        await Assert.That(result).IsEqualTo(6L);
    }
}