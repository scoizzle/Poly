using Poly.Interpretation.Vm;
using Poly.Interpretation.Vm.Instructions;

namespace Poly.Tests.Interpretation;

/// <summary>End-to-end tests for the register-based compiler pipeline.</summary>
public class NewCompilerBasicTests {
    static object? Run(Instruction[] instructions) {
        var loweringResult = new LoweringResult([.. instructions]);
        var program = ProgramCompiler.Compile(loweringResult, mode: CompilationMode.Normal);
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
            new BranchIfFalse(4),
            new LoadConst(1),
            new Jump(5),
            new LoadConst(2),
            new ReturnOp(),
        ]);
        await Assert.That(result).IsEqualTo(2L);
    }

    [Test]
    public async Task BranchTrue_Prog2() {
        var result = Run([
            new LoadConst(1),
            new BranchIfFalse(4),
            new LoadConst(10),
            new Jump(5),
            new LoadConst(20),
            new ReturnOp(),
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
}