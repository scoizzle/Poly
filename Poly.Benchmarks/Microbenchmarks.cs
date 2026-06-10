using BenchmarkDotNet.Attributes;

using Poly.Interpretation;
using Poly.Interpretation.VirtualMachine;

namespace Poly.Benchmarks;

[MemoryDiagnoser]
public class Microbenchmarks {
    private ValueStack _stack = null!;

    [IterationSetup]
    public void Setup() => _stack = new ValueStack();

    [IterationCleanup]
    public void Cleanup() => _stack.Dispose();

    [Benchmark]
    public long PushPopLong() {
        _stack.Push(42);
        return _stack.Pop();
    }

    [Benchmark]
    public long PushPopLong_Deep() {
        for (int i = 0; i < 100; i++) _stack.Push(i);
        long r = 0;
        for (int i = 0; i < 100; i++) r += _stack.Pop();
        return r;
    }

    private Heap _heap = null!;

    [Benchmark]
    public int HeapAllocGet() {
        int h = _heap.Allocate(42);
        return (int)_heap.Get(h)!;
    }

    [Benchmark]
    public int HeapAllocSet() {
        int h = _heap.Allocate(0);
        _heap.Set(h, 42);
        return (int)_heap.UnsafeGet(h)!;
    }

    [Benchmark]
    public int HeapAllocFreeReuse() {
        int h = _heap.Allocate(1);
        _heap.Set(h, null);
        h = _heap.Allocate(2);
        return (int)_heap.UnsafeGet(h)!;
    }

    // ── Dispatch: measure overhead of the instruction loop ──

    private VmState _state10 = null!;
    private VmState _state100 = null!;

    [Benchmark]
    public void Dispatch_10Pops() {
        _state10.Reset();
        Vm.Execute(_state10);
    }

    [Benchmark]
    public void Dispatch_100Pops() {
        _state100.Reset();
        Vm.Execute(_state100);
    }

    // ── Call: function call + return round-trip ──

    private VmState _callState = null!;

    [Benchmark]
    public void Call_NoArgs() {
        _callState.Reset();
        Vm.Execute(_callState);
    }

    // ── Closure: AllocateClosure + CallClosure round-trip ──

    private VmState _closureState = null!;

    [Benchmark]
    public void Closure_SingleCapture() {
        _closureState.Reset();
        Vm.Execute(_closureState);
    }

    [GlobalSetup]
    public void GlobalSetup() {
        _heap = new Heap();

        // ── Pop programs for dispatch benchmarks ──
        // Pop is a 1-byte nullary opcode (value 0)
        var code10 = new byte[10];
        Array.Fill<byte>(code10, (byte)OpCode.Pop);
        var prog10 = new Bytecode(code10, []);
        var code100 = new byte[100];
        Array.Fill<byte>(code100, (byte)OpCode.Pop);
        var prog100 = new Bytecode(code100, []);

        _state10 = new VmState { Program = prog10 };
        _state100 = new VmState { Program = prog100 };

        // ── Call program: function 0 returns 42 ──
        // Main: Push(42) [9 bytes], argCount=1 [1 byte Pop as 0], Call(0) [9 bytes], Return [1 byte]
        // Function 0 (offset 20): Push(42) [9 bytes], Return [1 byte]
        var callCode = new byte[30];
        int p = 0;
        callCode[p++] = (byte)((byte)OpCode.Push | OpCodeEncoding.SizeBit); // operand-bearing
        callCode[p++] = 42; callCode[p++] = 0; callCode[p++] = 0; callCode[p++] = 0;
        callCode[p++] = 0; callCode[p++] = 0; callCode[p++] = 0; callCode[p++] = 0; // value=42
        callCode[p++] = (byte)OpCode.Pop; // argSlots = 0 (use Pop as zero push)
        callCode[p] = (byte)((byte)OpCode.Call | OpCodeEncoding.SizeBit);
        p += 9;
        callCode[p++] = (byte)OpCode.Return;
        // Function 0 at offset 20
        callCode[p++] = (byte)((byte)OpCode.Push | OpCodeEncoding.SizeBit);
        callCode[p++] = 42; callCode[p++] = 0; callCode[p++] = 0; callCode[p++] = 0;
        callCode[p++] = 0; callCode[p++] = 0; callCode[p++] = 0; callCode[p++] = 0; // value=42
        callCode[p++] = (byte)OpCode.Return;
        var callProg = new Bytecode(callCode, [], [
            new FunctionEntry(PC: 20, ArgBytes: 0, RetBytes: 1, LocalCount: 0)
        ]);
        _callState = new VmState { Program = callProg };

        // ── Closure program placeholder (requires rewriting) ──
        var cloProg = new Bytecode([], []);
        _closureState = new VmState { Program = cloProg };
    }
}