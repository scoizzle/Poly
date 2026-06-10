using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Poly.Interpretation.VirtualMachine;

// ── VM ABI (Calling Convention & Stack Layout) ──────────────────────────
//
// The VM uses a slot-based stack where each slot is a long (8 bytes).
// All arithmetic operates on long slots; type coercion to int/double/etc.
// happens only in ExtractResult (final value read-out).
//
// ── Instruction encoding (1 byte opcode) ──
//   Bit 7 (0x80): InterruptBit — set by debugger for breakpoints
//   Bit 6 (0x40): SizeBit — 0 = 1-byte nullary, 1 = 9-byte operand-bearing
//   Bits 5-0 (0x3F): opcode value (max 64)
//
// ── Call / frame layout ──
// Before a Call* opcode, the stack must be:
//   [...stuff...][arg_0][arg_1]...[arg_N-1][argCount (N)]
//                                                  ^ spOff
// The Call* pops argCount (N), sets newBase = spOff, and writes a
// CallFrame struct at newBase (4 slots: RetPC, SavedBase, ArgSlots,
// RetSlots).  SP advances by 4 + LocalCount.  FrameBase = newBase.
// Local variables occupy slots immediately after the frame header.
//
// ── Lambda calling convention ──
// Lambdas reserve index 0 for the closure handle (or a dummy -1 when
// called directly via OpCode.Call rather than OpCode.CallClosure).
// Parameters are mapped starting at index 1.  The EmitInvoke path for
// direct Lambda calls pushes -1 as a dummy closure, so the arg layout
// matches what the lambda body expects:
//   [-1 (dummy closure)][user_arg_1]...[user_arg_N][N+1 (argCount)]
//
// ── Return convention ──
// OpCode.Return checks FrameBase:
//   < 0  → top-level return: force loop exit (codeOff = codeLength)
//   >= 0 → function return:
//     ref var frame = ref FrameAt(ref baseSlot, FrameBase)
//     preArg = FrameBase - frame.ArgSlots
//     Copy frame.RetSlots values from spOff - frame.RetSlots to preArg
//     spOff = preArg + frame.RetSlots
//     FrameBase = frame.SavedBase (handles -1 → top-level sentinel)
//     codeOff = frame.RetPC
//
// ── FrameBase sentinel ──
// -1 means "no active frame" (top-level execution).  Call* saves the
// current FrameBase verbatim (including -1) into SavedBase.  Return
// restores it.  This is how top-level Return detects the end of
// execution vs. a nested function return.

internal static class Vm {
    internal const int FrameHeaderSlots = 4;
    internal const int JitThreshold = 10;

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct CallFrame {
        public readonly long RetPC;
        public readonly long SavedBase;
        public readonly long ArgSlots;
        public readonly long RetSlots;
        public CallFrame(long retPC, long savedBase, long argSlots, long retSlots) {
            RetPC = retPC; SavedBase = savedBase; ArgSlots = argSlots; RetSlots = retSlots;
        }
    }

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
        static ref CallFrame FrameAt(ref long b, int frameBase) =>
            ref Unsafe.As<long, CallFrame>(ref Unsafe.Add(ref b, frameBase));

        try {
            while (codeOff < codeLength && !state.ShouldStop) {
                byte rawOp = Unsafe.Add(ref codeRef, codeOff);

                if (state.DebugMode && (rawOp & OpCodeEncoding.InterruptBit) != 0) {
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
                                codeOff = (int)Code64(ref codeRef, codeOff + 1);
                                continue;
                            }

                        case OpCode.JumpIfFalse:
                            if (Slot(ref baseSlot, --spOff) == 0) {
                                codeOff = (int)Code64(ref codeRef, codeOff + 1);
                            }
                            else {
                                codeOff += 9;
                                // Loop body JIT: check if this fall-through PC starts a hot loop body
                                if (!state.DebugMode && prog.LoopBodies.Count > 0)
                                    TryJitLoopBody(ref codeOff, state, prog);
                            }
                            continue;

                        case OpCode.Call: {
                                int funcIndex = (int)Code64(ref codeRef, codeOff + 1);
                                int argSlots = (int)Slot(ref baseSlot, --spOff);
                                var entry = prog.Functions[funcIndex];

                                // JIT path: native delegate dispatch
                                if (entry.NativeFn is not null && !state.DebugMode) {
                                    state.Stack.SetSP(spOff);
                                    state.PC = codeOff + 9;
                                    entry.NativeFn(state);
                                    spOff = state.Stack.SP;
                                    codeOff = state.PC;

                                    if (state.JITFallbackRequested) {
                                        state.JITFallbackRequested = false;
                                        Slot(ref baseSlot, spOff++) = argSlots; // restore argCount
                                        goto Call_Bytecode;
                                    }
                                    continue;
                                }

                                // Hotness threshold → compile native delegate
                                if (!state.DebugMode && entry.SourceNode is not null
                                    && entry.NativeFn is null && ++entry.HotCount > JitThreshold)
                                    entry.NativeFn = JitCompiler.Compile(entry, prog.AnalysisResult!);

                            Call_Bytecode:
                                int retPC = codeOff + 9;
                                int prevBase = state.FrameBase;
                                int newBase = spOff;
                                int totalSlots = FrameHeaderSlots + entry.LocalCount;
                                spOff += totalSlots;
                                FrameAt(ref baseSlot, newBase) = new CallFrame(
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

                        case OpCode.IncLocal: {
                                long packed = Code64(ref codeRef, codeOff + 1);
                                int index = (int)(packed >> 32);
                                long inc = (int)(packed & 0xFFFFFFFF);
                                if (state.FrameBase < 0)
                                    throw new InvalidOperationException("IncLocal outside of function frame");
                                int localOff = state.FrameBase + FrameHeaderSlots + index;
                                Slot(ref baseSlot, localOff) += inc;
                                Slot(ref baseSlot, spOff++) = Slot(ref baseSlot, localOff);
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

                        // ── Fused push+op (same opcode as nullary form, SizeBit distinguishes) ──

                        case OpCode.Add:
                            Slot(ref baseSlot, spOff - 1) += Code64(ref codeRef, codeOff + 1);
                            break;

                        case OpCode.Sub:
                            Slot(ref baseSlot, spOff - 1) -= Code64(ref codeRef, codeOff + 1);
                            break;

                        case OpCode.Mul:
                            Slot(ref baseSlot, spOff - 1) *= Code64(ref codeRef, codeOff + 1);
                            break;

                        case OpCode.Lt:
                            Slot(ref baseSlot, spOff - 1) =
                                Slot(ref baseSlot, spOff - 1) < Code64(ref codeRef, codeOff + 1) ? 1L : 0L;
                            break;

                        case OpCode.Gt:
                            Slot(ref baseSlot, spOff - 1) =
                                Slot(ref baseSlot, spOff - 1) > Code64(ref codeRef, codeOff + 1) ? 1L : 0L;
                            break;

                        case OpCode.Eq:
                            Slot(ref baseSlot, spOff - 1) =
                                Slot(ref baseSlot, spOff - 1) == Code64(ref codeRef, codeOff + 1) ? 1L : 0L;
                            break;

                        case OpCode.Le:
                            Slot(ref baseSlot, spOff - 1) =
                                Slot(ref baseSlot, spOff - 1) <= Code64(ref codeRef, codeOff + 1) ? 1L : 0L;
                            break;

                        case OpCode.Ge:
                            Slot(ref baseSlot, spOff - 1) =
                                Slot(ref baseSlot, spOff - 1) >= Code64(ref codeRef, codeOff + 1) ? 1L : 0L;
                            break;

                        case OpCode.Ne:
                            Slot(ref baseSlot, spOff - 1) =
                                Slot(ref baseSlot, spOff - 1) != Code64(ref codeRef, codeOff + 1) ? 1L : 0L;
                            break;

                        case OpCode.Not:
                            Slot(ref baseSlot, spOff - 1) =
                                Code64(ref codeRef, codeOff + 1) == 0 ? 1L : 0L;
                            break;

                        case OpCode.Neg:
                            Slot(ref baseSlot, spOff - 1) =
                                -Code64(ref codeRef, codeOff + 1);
                            break;

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
                                if (state.FrameBase < 0) {
                                    codeOff = codeLength; // force loop exit on top-level return
                                    break;
                                }
                                var frame = FrameAt(ref baseSlot, state.FrameBase);
                                int preArg = state.FrameBase - (int)frame.ArgSlots;
                                if (frame.RetSlots > 0 && preArg >= 0) {
                                    int retSrc = spOff - (int)frame.RetSlots;
                                    if (retSrc >= preArg) {
                                        long[] raw = state.Stack.RawSlots;
                                        Array.Copy(raw, retSrc, raw, preArg, (int)frame.RetSlots);
                                    }
                                }
                                int finalSp = preArg + (int)frame.RetSlots;
                                if (finalSp >= 0 && finalSp <= spOff)
                                    spOff = finalSp;
                                state.CachedArgSlots = (int)frame.ArgSlots;
                                state.FrameBase = (int)frame.SavedBase >= 0 ? (int)frame.SavedBase : -1;
                                codeOff = (int)frame.RetPC;
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

                                // JIT path: native delegate dispatch
                                if (entry.NativeFn is not null && !state.DebugMode) {
                                    state.Stack.SetSP(spOff);
                                    state.PC = codeOff + 1;
                                    entry.NativeFn(state);
                                    spOff = state.Stack.SP;
                                    codeOff = state.PC;

                                    if (state.JITFallbackRequested) {
                                        state.JITFallbackRequested = false;
                                        Slot(ref baseSlot, spOff++) = argSlots;
                                        goto CallClosure_Bytecode;
                                    }
                                    continue;
                                }

                                // Hotness threshold → compile native delegate
                                if (!state.DebugMode && entry.SourceNode is not null
                                    && entry.NativeFn is null && ++entry.HotCount > JitThreshold)
                                    entry.NativeFn = JitCompiler.Compile(entry, prog.AnalysisResult!);

                            CallClosure_Bytecode:
                                int retPC = codeOff + 1;
                                int prevBase = state.FrameBase;
                                int newBase = spOff;
                                spOff += FrameHeaderSlots + entry.LocalCount;
                                FrameAt(ref baseSlot, newBase) = new CallFrame(
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void TryJitLoopBody(ref int codeOff, VmState state, Bytecode prog) {
        var bodies = prog.LoopBodies;
        for (int i = 0; i < bodies.Count; i++) {
            var entry = bodies[i];
            if (codeOff != entry.BodyPC) continue;

            if (entry.NativeFn is not null && !state.DebugMode) {
                // JIT path: call native delegate
                var result = entry.NativeFn(state);
                codeOff = result switch {
                    LoopResult.Normal => entry.BodyPC + entry.BodyLength,
                    LoopResult.Break => entry.EndPC,
                    LoopResult.Continue => entry.ContinuePC,
                    _ => codeOff
                };
                return;
            }

            // Hotness threshold → compile native delegate
            if (state.DebugMode) return;
            if (entry.NativeFn is null && ++entry.HotCount > JitThreshold)
                entry.NativeFn = JitCompiler.CompileLoopBody(entry, prog.AnalysisResult!);
            return;
        }
    }
}