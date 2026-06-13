using Poly.Interpretation;
using Poly.Interpretation.VirtualMachine;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.Interpretation;

public class MicroOperationTests {
    private static readonly TestTraceWriter _trace = new();

    [Test]
    public async Task Push_OneValue_StackHasOneItem() {
        var uops = new MicroOp[] { new PushOp(42L) };
        var compiled = ProgramCompiler.Compile(uops);
        using var state = new VmState { Trace = _trace };
        compiled(state);
        await Assert.That(state.Stack.SP).IsEqualTo(1);
        await Assert.That(state.Stack.Pop()).IsEqualTo(42L);
    }

    [Test]
    public async Task Push_Then_Add_ProducesSum() {
        var uops = new MicroOp[] { new PushOp(10L), new PushOp(20L), new AddOp() };
        var compiled = ProgramCompiler.Compile(uops);
        using var state = new VmState { Trace = _trace };
        compiled(state);
        await Assert.That(state.Stack.Pop()).IsEqualTo(30L);
    }

    [Test]
    public async Task Push_Then_Sub_ProducesDifference() {
        var uops = new MicroOp[] { new PushOp(50L), new PushOp(20L), new SubOp() };
        var compiled = ProgramCompiler.Compile(uops);
        using var state = new VmState { Trace = _trace };
        compiled(state);
        await Assert.That(state.Stack.Pop()).IsEqualTo(30L);
    }

    [Test]
    public async Task Push_Then_Mul_ProducesProduct() {
        var uops = new MicroOp[] { new PushOp(7L), new PushOp(8L), new MulOp() };
        var compiled = ProgramCompiler.Compile(uops);
        using var state = new VmState { Trace = _trace };
        compiled(state);
        await Assert.That(state.Stack.Pop()).IsEqualTo(56L);
    }

    [Test]
    public async Task Push_Then_Div_ProducesQuotient() {
        var uops = new MicroOp[] { new PushOp(100L), new PushOp(4L), new DivOp() };
        var compiled = ProgramCompiler.Compile(uops);
        using var state = new VmState { Trace = _trace };
        compiled(state);
        await Assert.That(state.Stack.Pop()).IsEqualTo(25L);
    }

    [Test]
    public async Task Push_Then_Eq_True() {
        var uops = new MicroOp[] { new PushOp(10L), new PushOp(10L), new EqOp() };
        var compiled = ProgramCompiler.Compile(uops);
        using var state = new VmState { Trace = _trace };
        compiled(state);
        await Assert.That(state.Stack.Pop()).IsEqualTo(1L);
    }

    [Test]
    public async Task Push_Then_Eq_False() {
        var uops = new MicroOp[] { new PushOp(10L), new PushOp(20L), new EqOp() };
        var compiled = ProgramCompiler.Compile(uops);
        using var state = new VmState { Trace = _trace };
        compiled(state);
        await Assert.That(state.Stack.Pop()).IsEqualTo(0L);
    }

    [Test]
    public async Task Push_Then_Lt_True() {
        var uops = new MicroOp[] { new PushOp(5L), new PushOp(10L), new LtOp() };
        var compiled = ProgramCompiler.Compile(uops);
        using var state = new VmState { Trace = _trace };
        compiled(state);
        await Assert.That(state.Stack.Pop()).IsEqualTo(1L);
    }

    [Test]
    public async Task Push_Then_Dup_Duplicates() {
        var uops = new MicroOp[] { new PushOp(42L), new DupOp() };
        var compiled = ProgramCompiler.Compile(uops);
        using var state = new VmState { Trace = _trace };
        compiled(state);
        await Assert.That(state.Stack.SP).IsEqualTo(2);
        await Assert.That(state.Stack.Pop()).IsEqualTo(42L);
        await Assert.That(state.Stack.Pop()).IsEqualTo(42L);
    }

    [Test]
    public async Task Push_Then_Pop_RemovesItem() {
        var uops = new MicroOp[] { new PushOp(10L), new PushOp(20L), new PopOp() };
        var compiled = ProgramCompiler.Compile(uops);
        using var state = new VmState { Trace = _trace };
        compiled(state);
        await Assert.That(state.Stack.SP).IsEqualTo(1);
        await Assert.That(state.Stack.Pop()).IsEqualTo(10L);
    }

    [Test]
    public async Task Push_Then_Neg_Negates() {
        var uops = new MicroOp[] { new PushOp(42L), new NegOp() };
        var compiled = ProgramCompiler.Compile(uops);
        using var state = new VmState { Trace = _trace };
        compiled(state);
        await Assert.That(state.Stack.Pop()).IsEqualTo(-42L);
    }

    [Test]
    public async Task PushZero_Then_Not_ReturnsOne() {
        var uops = new MicroOp[] { new PushOp(0L), new NotOp() };
        var compiled = ProgramCompiler.Compile(uops);
        using var state = new VmState { Trace = _trace };
        compiled(state);
        await Assert.That(state.Stack.Pop()).IsEqualTo(1L);
    }

    [Test]
    public async Task Push_Then_AddImm_AddsInPlace() {
        var uops = new MicroOp[] { new PushOp(10L), new AddImmOp(5L) };
        var compiled = ProgramCompiler.Compile(uops);
        using var state = new VmState { Trace = _trace };
        compiled(state);
        await Assert.That(state.Stack.Pop()).IsEqualTo(15L);
    }

    [Test]
    public async Task Push_Then_LeImm_True() {
        var uops = new MicroOp[] { new PushOp(10L), new LeImmOp(10L) };
        var compiled = ProgramCompiler.Compile(uops);
        using var state = new VmState { Trace = _trace };
        compiled(state);
        await Assert.That(state.Stack.Pop()).IsEqualTo(1L);
    }

    [Test]
    public async Task Push_Then_LeImm_False() {
        var uops = new MicroOp[] { new PushOp(11L), new LeImmOp(10L) };
        var compiled = ProgramCompiler.Compile(uops);
        using var state = new VmState { Trace = _trace };
        compiled(state);
        await Assert.That(state.Stack.Pop()).IsEqualTo(0L);
    }

    [Test]
    public async Task Push_Then_EqImm_True() {
        var uops = new MicroOp[] { new PushOp(42L), new EqImmOp(42L) };
        var compiled = ProgramCompiler.Compile(uops);
        using var state = new VmState { Trace = _trace };
        compiled(state);
        await Assert.That(state.Stack.Pop()).IsEqualTo(1L);
    }

    [Test]
    public async Task IncLocal_IncrementsLocal() {
        // Simulate call frame: FB=0, CAS=1, local[0] starts at 0
        var state = new VmState { FrameBase = 0, CachedArgSlots = 1, Trace = _trace };
        state.Stack.SetSP(3);          // SP after call setup: FB + CAS + LC + 2 = 0+1+1+2 = 4? wait
        // Actually: after call with FB=0, CAS=1, LC=1: SP = 0+1+1+2 = 4
        // Let's just set SP to where local[0] is accessible
        state.Stack.RawSlots[2] = 0;    // local[0] at FB + CAS + 1 + 0 = 2
        state.Stack.SetSP(3);

        var uops = new MicroOp[] { new IncLocalOp(0, 1L) };
        var compiled = ProgramCompiler.Compile(uops);
        compiled(state);

        await Assert.That(state.Stack.RawSlots[2]).IsEqualTo(1L);
    }

    [Test]
    public async Task LoadArg_LoadsCorrectArgument() {
        var state = new VmState { FrameBase = 10, CachedArgSlots = 2, Trace = _trace };
        state.Stack.RawSlots[10] = 99L; // arg[0]
        state.Stack.RawSlots[11] = 42L; // arg[1]
        state.Stack.SetSP(12);

        var uops = new MicroOp[] { new LoadArgOp(1) }; // load arg[1]
        var compiled = ProgramCompiler.Compile(uops);
        compiled(state);

        await Assert.That(state.Stack.Pop()).IsEqualTo(42L);
    }

    [Test]
    public async Task CmpLocalLeOp_True_WithoutStackTemporary() {
        var state = new VmState { FrameBase = 0, CachedArgSlots = 1, Trace = _trace };
        state.Stack.RawSlots[2] = 5L;    // local[0] = 5
        state.Stack.SetSP(3);

        var uops = new MicroOp[] { new CmpLocalLeOp(0, 10L) };
        var compiled = ProgramCompiler.Compile(uops);
        compiled(state);

        await Assert.That(state.Stack.Pop()).IsEqualTo(1L);
    }

    [Test]
    public async Task CmpLocalLeOp_False_WithoutStackTemporary() {
        var state = new VmState { FrameBase = 0, CachedArgSlots = 1, Trace = _trace };
        state.Stack.RawSlots[2] = 15L;   // local[0] = 15
        state.Stack.SetSP(3);

        var uops = new MicroOp[] { new CmpLocalLeOp(0, 10L) };
        var compiled = ProgramCompiler.Compile(uops);
        compiled(state);

        await Assert.That(state.Stack.Pop()).IsEqualTo(0L);
    }

    [Test]
    public async Task MultiplePushes_Then_Adds_CorrectResult() {
        var uops = new MicroOp[] {
            new PushOp(3L), new PushOp(5L), new MulOp(),   // 3*5=15
            new PushOp(2L), new MulOp(),                    // 15*2=30
            new PushOp(2L), new MulOp(),                    // 30*2=60
            new PushOp(3L), new AddOp()                     // 60+3=63
        };
        var compiled = ProgramCompiler.Compile(uops);
        using var state = new VmState { Trace = _trace };
        compiled(state);
        await Assert.That(state.Stack.Pop()).IsEqualTo(63L);
    }
}