using Poly.Interpretation.Vm;
using Poly.Interpretation.Vm.Instructions;

namespace Poly.Tests.Interpretation;

/// <summary>Tests invariants of the register-based compiler pipeline.</summary>
public class NewCompilerInvariantTests {
    static object? Run(Instruction[] instructions) {
        var loweringResult = new LoweringResult([.. instructions]);
        var program = ProgramCompiler.Compile(loweringResult, mode: CompilationMode.Normal);
        var state = new VmState(program);
        program.Delegate(state);
        return state.Stack.StackPointer > 0
            ? (object?)state.Stack.RawSlots[0]
            : null;
    }

    [Test]
    public async Task Compile_ReturnOp_MakesValueAvailable() {
        var result = Run([
            new LoadConst(99),
            new ReturnOp(),
        ]);
        await Assert.That(result).IsEqualTo(99L);
    }

    [Test]
    public async Task Compile_BranchIfFalse_NotLastInstruction() {
        var result = Run([
            new LoadConst(0),
            new BranchIfFalse(2) { ConsumedFromPcs = [0] },
            new ReturnOp() { ConsumedFromPcs = [0] },
        ]);
        await Assert.That(result).IsEqualTo(0L);
    }

    [Test]
    public async Task BinOp_ImmediateForm_RequiresOneInput() {
        var result = Run([
            new LoadConst(10),
            new BinOp(BinOpKind.Add, Immediate: 5),
            new ReturnOp(),
        ]);
        await Assert.That(result).IsEqualTo(15L);
    }

    [Test]
    public async Task BinOp_NonImmediateForm_RequiresTwoInputs() {
        var result = Run([
            new LoadConst(7),
            new LoadConst(3),
            new BinOp(BinOpKind.Div),
            new ReturnOp(),
        ]);
        await Assert.That(result).IsEqualTo(2L);
    }

    [Test]
    public async Task ProducerChain_OperandRegistersAreDistinct() {
        var result = Run([
            new LoadConst(2),
            new LoadConst(3),
            new LoadConst(4),
            new BinOp(BinOpKind.Add),
            new BinOp(BinOpKind.Mul),
            new ReturnOp(),
        ]);
        await Assert.That(result).IsEqualTo(14L);
    }

    [Test]
    public async Task BranchValue_MergesCorrectly() {
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
    public async Task MaxActiveLocalDepth_SpillsToStack() {
        // Create enough simultaneous live values to exceed the default bound
        var instructions = new List<Instruction>();
        for (int i = 0; i < 20; i++)
            instructions.Add(new LoadConst(i));
        instructions.Add(new ReturnOp());

        var result = Run([.. instructions]);
        await Assert.That(result).IsEqualTo(19L);
    }
}