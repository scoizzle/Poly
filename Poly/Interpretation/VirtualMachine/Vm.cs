using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Poly.Interpretation.VirtualMachine;

internal static class Vm {
    internal const int FrameHeaderSlots = 4;

    public static InterpreterResult Execute(VmState state) {
        var prog = state.Program;
        if (prog is null) {
            state.Complete(InterpreterResult.Void);
            return InterpreterResult.Void;
        }

        state.Status = InterpreterStatus.Running;

        // Pre-load constants into heap
        for (int i = 0; i < prog.Constants.Count; i++)
            state.Heap.Allocate(prog.Constants[i]);

        var rawCode = prog.Code;
        var code = rawCode.AsSpan();
        int codeOff = state.PC;
        int codeLength = code.Length;
        ref byte codeRef = ref MemoryMarshal.GetReference(code);

        ref long baseSlot = ref MemoryMarshal.GetReference(
            state.Stack.RawSlots.AsSpan());
        int spOff = state.Stack.SP;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static ref long Slot(ref long b, int o) => ref Unsafe.Add(ref b, o);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static long Code64(ref byte b, int o) =>
            Unsafe.ReadUnaligned<long>(ref Unsafe.Add(ref b, o));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static (int retPC, int savedBase, int argSlots, int retSlots)
            ReadFrame(ref long b, int off) => (
            (int)Unsafe.Add(ref b, off),
            (int)Unsafe.Add(ref b, off + 1),
            (int)Unsafe.Add(ref b, off + 2),
            (int)Unsafe.Add(ref b, off + 3));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void WriteFrame(ref long b, int off,
            int retPC, int savedBase, int argSlots, int retSlots) {
            Unsafe.Add(ref b, off) = retPC;
            Unsafe.Add(ref b, off + 1) = savedBase;
            Unsafe.Add(ref b, off + 2) = argSlots;
            Unsafe.Add(ref b, off + 3) = retSlots;
        }

        try {
            while (codeOff < codeLength && !state.ShouldStop) {
                byte rawOp = Unsafe.Add(ref codeRef, codeOff);

                if ((rawOp & OpCodeEncoding.InterruptBit) != 0) {
                    state.Status = InterpreterStatus.Suspended;
                    break;
                }

                if ((rawOp & OpCodeEncoding.SizeBit) != 0) {
                    // ── 9-byte operand-bearing ──
                    switch ((OpCode)(rawOp & OpCodeEncoding.OpcodeMask)) {
                        case OpCode.Push:
                            Slot(ref baseSlot, spOff++) = Code64(ref codeRef, codeOff + 1);
                            break;

                        case OpCode.Jump: {
                                long target = Code64(ref codeRef, codeOff + 1);
                                codeOff = (int)target * 9;
                                continue;
                            }

                        case OpCode.JumpIfFalse:
                            if (Slot(ref baseSlot, --spOff) == 0) {
                                long target = Code64(ref codeRef, codeOff + 1);
                                codeOff = (int)target * 9;
                            }
                            else {
                                codeOff += 9;
                            }
                            continue;

                        case OpCode.Call: {
                                int funcIndex = (int)Code64(ref codeRef, codeOff + 1);
                                int argSlots = (int)Slot(ref baseSlot, --spOff);
                                var entry = prog.Functions[funcIndex];
                                int retPC = codeOff + 9;
                                int prevBase = state.FrameBase < 0 ? 0 : state.FrameBase;
                                int newBase = spOff;
                                int totalSlots = FrameHeaderSlots + entry.LocalCount;
                                spOff += totalSlots;
                                WriteFrame(ref baseSlot, newBase,
                                    retPC, prevBase, argSlots, entry.RetBytes);
                                state.FrameBase = newBase;
                                state.CachedArgSlots = argSlots;
                                codeOff = entry.PC;
                                continue;
                            }

                        case OpCode.CallExternal: {
                                int siteIndex = (int)Code64(ref codeRef, codeOff + 1);
                                if ((uint)siteIndex >= (uint)prog.CallSites.Count ||
                                    prog.CallSites[siteIndex] is null)
                                    throw new InvalidOperationException(
                                        $"CallExternal: no target at site {siteIndex}");
                                state.Stack.SetSP(spOff);
                                state.PC = codeOff + 9;
                                prog.CallSites[siteIndex](state);
                                spOff = state.Stack.SP;
                                codeOff = state.PC;
                                continue;
                            }

                        case OpCode.AllocClosure: {
                                long packed = Code64(ref codeRef, codeOff + 1);
                                int funcIndex = (int)(packed & 0xFFFFFFFF);
                                int captureCount = (int)(packed >> 32);
                                var closure = new Closure(funcIndex, captureCount);
                                for (int i = captureCount - 1; i >= 0; i--)
                                    closure.Captures[i] = Slot(ref baseSlot, --spOff);
                                int handle = state.Heap.Allocate(closure);
                                Slot(ref baseSlot, spOff++) = handle;
                                break;
                            }

                        case OpCode.LoadArg: {
                                int index = (int)Code64(ref codeRef, codeOff + 1);
                                if (state.FrameBase < 0)
                                    throw new InvalidOperationException("LoadArg outside of function frame");
                                int argStart = state.FrameBase - state.CachedArgSlots;
                                Slot(ref baseSlot, spOff++) = Slot(ref baseSlot, argStart + index);
                                break;
                            }

                        case OpCode.StoreArg: {
                                int index = (int)Code64(ref codeRef, codeOff + 1);
                                if (state.FrameBase < 0)
                                    throw new InvalidOperationException("StoreArg outside of function frame");
                                int argStart = state.FrameBase - state.CachedArgSlots;
                                Slot(ref baseSlot, argStart + index) = Slot(ref baseSlot, --spOff);
                                break;
                            }

                        case OpCode.LoadLocal: {
                                int index = (int)Code64(ref codeRef, codeOff + 1);
                                if (state.FrameBase < 0)
                                    throw new InvalidOperationException("LoadLocal outside of function frame");
                                int localOff = state.FrameBase + FrameHeaderSlots + index;
                                Slot(ref baseSlot, spOff++) = Slot(ref baseSlot, localOff);
                                break;
                            }

                        case OpCode.StoreLocal: {
                                int index = (int)Code64(ref codeRef, codeOff + 1);
                                if (state.FrameBase < 0)
                                    throw new InvalidOperationException("StoreLocal outside of function frame");
                                int localOff = state.FrameBase + FrameHeaderSlots + index;
                                Slot(ref baseSlot, localOff) = Slot(ref baseSlot, --spOff);
                                break;
                            }

                        case OpCode.LoadUpvalue: {
                                int upvalueIndex = (int)Code64(ref codeRef, codeOff + 1);
                                if (state.FrameBase < 0)
                                    throw new InvalidOperationException("LoadUpvalue outside of function frame");
                                int closureHandle = (int)Slot(ref baseSlot,
                                    state.FrameBase - state.CachedArgSlots);
                                var closure = state.Heap.Get(closureHandle) as Closure
                                    ?? throw new InvalidOperationException("LoadUpvalue: no closure at arg 0");
                                long val = closure.Captures is not null &&
                                    upvalueIndex < closure.Captures.Length &&
                                    closure.Captures[upvalueIndex] is long lv
                                    ? lv : 0;
                                Slot(ref baseSlot, spOff++) = val;
                                break;
                            }

                        case OpCode.StoreUpvalue: {
                                int upvalueIndex = (int)Code64(ref codeRef, codeOff + 1);
                                if (state.FrameBase < 0)
                                    throw new InvalidOperationException("StoreUpvalue outside of function frame");
                                int closureHandle = (int)Slot(ref baseSlot,
                                    state.FrameBase - state.CachedArgSlots);
                                var closure = state.Heap.Get(closureHandle) as Closure
                                    ?? throw new InvalidOperationException("StoreUpvalue: no closure at arg 0");
                                if (closure.Captures is null || upvalueIndex >= closure.Captures.Length)
                                    throw new InvalidOperationException(
                                        $"StoreUpvalue: index {upvalueIndex} out of range");
                                closure.Captures[upvalueIndex] = Slot(ref baseSlot, --spOff);
                                break;
                            }

                        default:
                            throw new InvalidOperationException(
                                $"Unsupported operand-bearing opcode: 0x{rawOp & OpCodeEncoding.OpcodeMask:X2}");
                    }
                    codeOff += 9;
                }
                else {
                    // ── 1-byte nullary ──
                    switch ((OpCode)(rawOp & OpCodeEncoding.OpcodeMask)) {
                        case OpCode.Pop:
                            spOff--;
                            break;

                        case OpCode.Dup: {
                                long v = Slot(ref baseSlot, spOff - 1);
                                Slot(ref baseSlot, spOff++) = v;
                                break;
                            }

                        case OpCode.Neg:
                            Slot(ref baseSlot, spOff - 1) = -Slot(ref baseSlot, spOff - 1);
                            break;

                        case OpCode.Not:
                            Slot(ref baseSlot, spOff - 1) =
                                Slot(ref baseSlot, spOff - 1) == 0 ? 1 : 0;
                            break;

                        case OpCode.Add:
                            Slot(ref baseSlot, spOff - 2) =
                                Slot(ref baseSlot, spOff - 2) + Slot(ref baseSlot, spOff - 1);
                            spOff--;
                            break;

                        case OpCode.Sub:
                            Slot(ref baseSlot, spOff - 2) =
                                Slot(ref baseSlot, spOff - 2) - Slot(ref baseSlot, spOff - 1);
                            spOff--;
                            break;

                        case OpCode.Mul:
                            Slot(ref baseSlot, spOff - 2) =
                                Slot(ref baseSlot, spOff - 2) * Slot(ref baseSlot, spOff - 1);
                            spOff--;
                            break;

                        case OpCode.Div: {
                                long right = Slot(ref baseSlot, spOff - 1);
                                if (right == 0)
                                    throw new DivideByZeroException("Division by zero");
                                Slot(ref baseSlot, spOff - 2) =
                                    Slot(ref baseSlot, spOff - 2) / right;
                                spOff--;
                                break;
                            }

                        case OpCode.DivRem: {
                                long right = Slot(ref baseSlot, spOff - 1);
                                if (right == 0)
                                    throw new DivideByZeroException("Division by zero");
                                long left = Slot(ref baseSlot, spOff - 2);
                                Slot(ref baseSlot, spOff - 2) = left / right;
                                Slot(ref baseSlot, spOff - 1) = left % right;
                                break;
                            }

                        case OpCode.Eq:
                            Slot(ref baseSlot, spOff - 2) =
                                Slot(ref baseSlot, spOff - 2) == Slot(ref baseSlot, spOff - 1) ? 1 : 0;
                            spOff--;
                            break;

                        case OpCode.Ne:
                            Slot(ref baseSlot, spOff - 2) =
                                Slot(ref baseSlot, spOff - 2) != Slot(ref baseSlot, spOff - 1) ? 1 : 0;
                            spOff--;
                            break;

                        case OpCode.Lt:
                            Slot(ref baseSlot, spOff - 2) =
                                Slot(ref baseSlot, spOff - 2) < Slot(ref baseSlot, spOff - 1) ? 1 : 0;
                            spOff--;
                            break;

                        case OpCode.Le:
                            Slot(ref baseSlot, spOff - 2) =
                                Slot(ref baseSlot, spOff - 2) <= Slot(ref baseSlot, spOff - 1) ? 1 : 0;
                            spOff--;
                            break;

                        case OpCode.Gt:
                            Slot(ref baseSlot, spOff - 2) =
                                Slot(ref baseSlot, spOff - 2) > Slot(ref baseSlot, spOff - 1) ? 1 : 0;
                            spOff--;
                            break;

                        case OpCode.Ge:
                            Slot(ref baseSlot, spOff - 2) =
                                Slot(ref baseSlot, spOff - 2) >= Slot(ref baseSlot, spOff - 1) ? 1 : 0;
                            spOff--;
                            break;

                        case OpCode.BitNot:
                            Slot(ref baseSlot, spOff - 1) = ~Slot(ref baseSlot, spOff - 1);
                            break;

                        case OpCode.BitAnd:
                            Slot(ref baseSlot, spOff - 2) =
                                Slot(ref baseSlot, spOff - 2) & Slot(ref baseSlot, spOff - 1);
                            spOff--;
                            break;

                        case OpCode.BitOr:
                            Slot(ref baseSlot, spOff - 2) =
                                Slot(ref baseSlot, spOff - 2) | Slot(ref baseSlot, spOff - 1);
                            spOff--;
                            break;

                        case OpCode.BitXor:
                            Slot(ref baseSlot, spOff - 2) =
                                Slot(ref baseSlot, spOff - 2) ^ Slot(ref baseSlot, spOff - 1);
                            spOff--;
                            break;

                        case OpCode.Shl:
                            Slot(ref baseSlot, spOff - 2) =
                                Slot(ref baseSlot, spOff - 2) << (int)Slot(ref baseSlot, spOff - 1);
                            spOff--;
                            break;

                        case OpCode.Shr:
                            Slot(ref baseSlot, spOff - 2) =
                                Slot(ref baseSlot, spOff - 2) >> (int)Slot(ref baseSlot, spOff - 1);
                            spOff--;
                            break;

                        // ── Frames ──

                        case OpCode.Return: {
                                if (state.FrameBase < 0)
                                    break;
                                var (retPC, savedBase, argSlots, retSlots) =
                                    ReadFrame(ref baseSlot, state.FrameBase);
                                int preArg = state.FrameBase - argSlots;
                                if (retSlots > 0 && preArg >= 0) {
                                    int retSrc = spOff - retSlots;
                                    if (retSrc >= preArg) {
                                        long[] raw = state.Stack.RawSlots;
                                        Array.Copy(raw, retSrc, raw, preArg, retSlots);
                                    }
                                }
                                int finalSp = preArg + retSlots;
                                if (finalSp >= 0 && finalSp <= spOff)
                                    spOff = finalSp;
                                state.CachedArgSlots = argSlots;
                                state.FrameBase = savedBase >= 0 ? savedBase : -1;
                                codeOff = retPC;
                                continue;
                            }

                        // ── Heap indirection ──

                        case OpCode.LoadValue: {
                                int handle = (int)Slot(ref baseSlot, --spOff);
                                if (handle >= 0) {
                                    var obj = state.Heap.Get(handle);
                                    Slot(ref baseSlot, spOff++) = obj is long lv ? lv : 0;
                                }
                                else {
                                    Slot(ref baseSlot, spOff++) = Slot(ref baseSlot, -handle);
                                }
                                break;
                            }

                        case OpCode.StoreValue: {
                                int handle = (int)Slot(ref baseSlot, --spOff);
                                long value = Slot(ref baseSlot, --spOff);
                                if (handle >= 0)
                                    state.Heap.Set(handle, value);
                                else
                                    Slot(ref baseSlot, -handle) = value;
                                break;
                            }

                        // ── Closures ──

                        case OpCode.CallClosure: {
                                int argSlots = (int)Slot(ref baseSlot, --spOff);
                                int closureHandle = (int)Slot(ref baseSlot, spOff - argSlots);
                                var closure = state.Heap.Get(closureHandle) as Closure
                                    ?? throw new InvalidOperationException("CallClosure target is not a Closure");
                                var entry = prog.Functions[closure.FuncIndex];
                                int retPC = codeOff + 1;
                                int prevBase = state.FrameBase < 0 ? 0 : state.FrameBase;
                                int newBase = spOff;
                                spOff += FrameHeaderSlots + entry.LocalCount;
                                WriteFrame(ref baseSlot, newBase,
                                    retPC, prevBase, argSlots, entry.RetBytes);
                                state.FrameBase = newBase;
                                state.CachedArgSlots = argSlots;
                                codeOff = entry.PC;
                                continue;
                            }

                        // ── Exceptions ──

                        case OpCode.Throw: {
                                int exVal = (int)Slot(ref baseSlot, --spOff);
                                var region = FindRegion(prog.ExceptionRegions, codeOff);
                                if (region is not null) {
                                    if (region.CatchStart >= 0) {
                                        Slot(ref baseSlot, spOff++) = exVal;
                                        state.PendingExceptionValue = null;
                                        codeOff = region.CatchStart;
                                    }
                                    else if (region.FinallyStart is not null) {
                                        state.PendingExceptionValue = exVal;
                                        codeOff = region.FinallyStart.Value;
                                    }
                                }
                                else {
                                    throw new InvalidOperationException("Unhandled VM exception: " + exVal);
                                }
                                continue;
                            }

                        case OpCode.EndFinally: {
                                if (state.PendingExceptionValue is not null) {
                                    int exVal = state.PendingExceptionValue.Value;
                                    state.PendingExceptionValue = null;
                                    var region = FindRegion(prog.ExceptionRegions, codeOff);
                                    if (region is not null && region.CatchStart >= 0) {
                                        Slot(ref baseSlot, spOff++) = exVal;
                                        codeOff = region.CatchStart;
                                    }
                                    else {
                                        throw new InvalidOperationException(
                                            "Unhandled VM exception: " + exVal);
                                    }
                                }
                                continue;
                            }

                        default:
                            throw new InvalidOperationException(
                                $"Unsupported nullary opcode: 0x{rawOp & OpCodeEncoding.OpcodeMask:X2}");
                    }
                    codeOff++;
                }
            }

            state.Stack.SetSP(spOff);
            state.PC = codeOff;

            if (state.IsSuspended) {
                var suspendResult = InterpreterResult.Suspend();
                state.SetLastResultWithoutChangingStatus(suspendResult);
                return suspendResult;
            }

            var finalResult = ExtractResult(state, prog, ref baseSlot, ref spOff);
            if (!state.IsComplete && !state.IsSuspended)
                state.Complete(finalResult);
            return finalResult;
        }
        catch (Exception ex) {
            state.Stack.SetSP(spOff);
            state.PC = codeOff;
            var err = InterpreterResult.Throw(ex);
            state.Complete(err);
            return err;
        }
    }

    private static ExceptionRegion? FindRegion(
        IReadOnlyList<ExceptionRegion> regions, int pc) {
        for (int i = 0; i < regions.Count; i++) {
            var r = regions[i];
            if (pc >= r.TryStart && pc < r.TryEnd)
                return r;
        }
        return null;
    }

    private static InterpreterResult ExtractResult(
        VmState state, Bytecode prog,
        ref long baseSlot, ref int spOff) {
        if (spOff == 0)
            return InterpreterResult.Void;

        var resultType = prog.ResultType;
        long raw = Unsafe.Add(ref baseSlot, --spOff);

        if (resultType is null || resultType == typeof(void))
            return InterpreterResult.FromValue(raw);

        if (resultType == typeof(int) || resultType == typeof(long)
            || resultType == typeof(uint) || resultType == typeof(ulong))
            return InterpreterResult.FromValue(raw);

        if (resultType == typeof(double) || resultType == typeof(float))
            return InterpreterResult.FromValue(
                BitConverter.Int64BitsToDouble(raw));

        if (resultType == typeof(bool))
            return InterpreterResult.FromValue(raw != 0);

        return InterpreterResult.FromValue(raw);
    }

    internal static object? ResolveHeapValue(VmState state, int raw) =>
        raw >= 0 && raw < state.Heap.Count ? state.Heap.UnsafeGet(raw) : (object?)raw;

    internal static bool IsValidHeapHandle(VmState state, int handle) =>
        handle >= 0 && handle < state.Heap.Count;
}