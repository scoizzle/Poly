using System.Runtime.InteropServices;

using Poly.Interpretation;

namespace Poly.Interpretation.VirtualMachine;

internal static class Vm {
    private static readonly string[] OpcodeNames = Enum.GetNames<OpCode>();

    public static InterpreterResult Execute(VmState state) {
        if (state.Program is null) {
            state.Complete(InterpreterResult.Void);
            return InterpreterResult.Void;
        }

        var prog = state.Program;
        var code = prog.Code;
        state.Status = InterpreterStatus.Running;

#if DEBUG
        var trace = state.Trace;
        if (trace is not null)
            state.Heap.OnAllocate = (handle, value) => {
                string desc = value switch {
                    null => "null",
                    string s => $"\"{s}\"",
                    int iv => iv.ToString(),
                    Closure c => $"Closure[func={c.FuncIndex} cap={c.Captures?.Length}]",
                    _ => value.GetType().Name
                };
                trace.WriteLine($"  → alloc H[{handle}] {desc}");
            };
#endif

        for (int i = 0; i < prog.Constants.Count; i++)
            state.Heap.Allocate(prog.Constants[i]);

        int pc = state.PC;
        const int MaxSteps = 100_000;
        int steps = 0;

        try {
            while (pc < code.Length && !state.IsSuspended && !state.IsComplete) {
                if (++steps > MaxSteps)
                    throw new InvalidOperationException("Max instruction steps exceeded (possible infinite loop in IR)");

                int instrPc = pc;
                var op = (OpCode)code[pc++];

#if DEBUG
                if (trace is not null) {
                    string opName = (int)op < OpcodeNames.Length ? OpcodeNames[(int)op] : $"0x{(int)op:X2}";
                    string nodeInfo = "";
                    if (prog.SourceMap is not null && prog.SourceMap.TryGetValue(instrPc, out var nodeId)) {
                        string desc = state.NodeDescriptions is not null && state.NodeDescriptions.TryGetValue(nodeId, out var d)
                            ? TruncateTrace(d) : $"#{nodeId}";
                        nodeInfo = $" {desc}";
                    }
                    trace.WriteLine($"PC:{instrPc:D4}{nodeInfo} {opName,-14} {state.FormatStack()}");
                }
#endif

                if (state.BreakpointPCs is not null && state.BreakpointPCs.Contains(instrPc)) {
                    state.SavedPC = pc;
                    state.Status = InterpreterStatus.Suspended;
                    return InterpreterResult.Suspend();
                }

                switch (op) {
                    case OpCode.Nop:
                        break;

                    case OpCode.Dup: {
                            var val = state.Stack.PeekInt();
                            state.Stack.Push(val);
                            break;
                        }

                    case OpCode.Pop:
                        state.Stack.PopInt();
                        break;

                    case OpCode.PushInt:
                        state.Stack.Push(ReadInt32(code, ref pc));
                        break;

                    case OpCode.PushLong:
                        state.Stack.Push(ReadInt64(code, ref pc));
                        break;

                    case OpCode.LoadConst: {
                            int idx = ReadInt32(code, ref pc);
                            state.Stack.Push(idx);
                            break;
                        }

                    case OpCode.PushDouble:
                        state.Stack.Push(ReadDouble(code, ref pc));
                        break;

                    case OpCode.LoadArg: {
                            int paramIndex = ReadInt32(code, ref pc);
                            if (state.FrameBase < 0)
                                throw new InvalidOperationException("LoadArg outside of function frame");
                            var hdr = FrameHeader.Read(state.Stack.AsSpan(), state.FrameBase);
                            int argStart = state.FrameBase - hdr.ArgSlots;
                            int val = state.Stack.AsSpan()[argStart + paramIndex];
                            state.Stack.Push(val);
                            break;
                        }

                    case OpCode.LoadLocal: {
                            int localIndex = ReadInt32(code, ref pc);
                            if (state.FrameBase < 0)
                                throw new InvalidOperationException("LoadLocal outside of function frame");
                            int localOff = state.FrameBase + FrameHeader.SlotCount + localIndex;
                            int val = state.Stack.AsSpan()[localOff];
                            state.Stack.Push(val);
                            break;
                        }

                    case OpCode.StoreLocal: {
                            int localIndex = ReadInt32(code, ref pc);
                            int value = state.Stack.PopInt();
                            if (state.FrameBase < 0)
                                throw new InvalidOperationException("StoreLocal outside of function frame");
                            int localOff = state.FrameBase + FrameHeader.SlotCount + localIndex;
                            state.Stack.AsSpan()[localOff] = value;
                            break;
                        }

                    case OpCode.LoadValue: {
                            var (handle, size) = state.Stack.Pop<(int handle, int size)>();
                            if (handle >= 0) {
                                var obj = state.Heap.Get(handle);
                                state.Stack.Push(obj is int iv ? iv : 0);
                            }
                            else {
                                int src = -handle;
                                var srcSpan = state.Stack.AsSpan().Slice(src, size);
                                int val = srcSpan[0];
                                state.Stack.Push(val);
                            }
                            break;
                        }

                    case OpCode.StoreValue: {
                            var (handle, size) = state.Stack.Pop<(int handle, int size)>();
                            int curSp = state.Stack.SP;
                            int srcOff = curSp - size;
                            if (srcOff < 0)
                                throw new InvalidOperationException("Not enough values for StoreValue source");
                            var srcVal = state.Stack.AsSpan()[srcOff];
                            if (handle >= 0) {
                                state.Heap.Set(handle, srcVal);
                            }
                            else {
                                int dest = -handle;
                                state.Stack.AsSpan()[dest] = srcVal;
                            }
                            state.Stack.Drop(size);
                            break;
                        }

                    case OpCode.Add: {
                            var (left, right) = state.Stack.Pop<(int left, int right)>();
                            state.Stack.Push(left + right);
                            break;
                        }

                    case OpCode.Sub: {
                            var (left, right) = state.Stack.Pop<(int left, int right)>();
                            state.Stack.Push(left - right);
                            break;
                        }

                    case OpCode.Mul: {
                            var (left, right) = state.Stack.Pop<(int left, int right)>();
                            state.Stack.Push(left * right);
                            break;
                        }

                    case OpCode.Div: {
                            var (left, right) = state.Stack.Pop<(int left, int right)>();
                            if (right == 0) {
                                if (FindRegion(prog.ExceptionRegions, instrPc) is not null) {
                                    state.Stack.Push(-1);
                                    goto case OpCode.Throw;
                                }
                                throw new DivideByZeroException("Division by zero");
                            }
                            state.Stack.Push(left / right);
                            break;
                        }

                    case OpCode.Mod: {
                            var (left, right) = state.Stack.Pop<(int left, int right)>();
                            if (right == 0) {
                                if (FindRegion(prog.ExceptionRegions, instrPc) is not null) {
                                    state.Stack.Push(-1);
                                    goto case OpCode.Throw;
                                }
                                throw new DivideByZeroException("Division by zero");
                            }
                            state.Stack.Push(left % right);
                            break;
                        }

                    case OpCode.Neg: {
                            var a = state.Stack.PopInt();
                            state.Stack.Push(-a);
                            break;
                        }

                    case OpCode.UDiv: {
                            var (left, right) = state.Stack.Pop<(uint left, uint right)>();
                            if (right == 0) throw new DivideByZeroException("Division by zero");
                            state.Stack.Push((int)(left / right));
                            break;
                        }

                    case OpCode.UMod: {
                            var (left, right) = state.Stack.Pop<(uint left, uint right)>();
                            if (right == 0) throw new DivideByZeroException("Division by zero");
                            state.Stack.Push((int)(left % right));
                            break;
                        }

                    case OpCode.Eq: {
                            var (left, right) = state.Stack.Pop<(int left, int right)>();
                            state.Stack.Push(left == right ? 1 : 0);
                            break;
                        }

                    case OpCode.Ne: {
                            var (left, right) = state.Stack.Pop<(int left, int right)>();
                            state.Stack.Push(left != right ? 1 : 0);
                            break;
                        }

                    case OpCode.Lt: {
                            var (left, right) = state.Stack.Pop<(int left, int right)>();
                            state.Stack.Push(left < right ? 1 : 0);
                            break;
                        }

                    case OpCode.Le: {
                            var (left, right) = state.Stack.Pop<(int left, int right)>();
                            state.Stack.Push(left <= right ? 1 : 0);
                            break;
                        }

                    case OpCode.Gt: {
                            var (left, right) = state.Stack.Pop<(int left, int right)>();
                            state.Stack.Push(left > right ? 1 : 0);
                            break;
                        }

                    case OpCode.Ge: {
                            var (left, right) = state.Stack.Pop<(int left, int right)>();
                            state.Stack.Push(left >= right ? 1 : 0);
                            break;
                        }

                    case OpCode.ULt: {
                            var (left, right) = state.Stack.Pop<(uint left, uint right)>();
                            state.Stack.Push(left < right ? 1 : 0);
                            break;
                        }

                    case OpCode.ULe: {
                            var (left, right) = state.Stack.Pop<(uint left, uint right)>();
                            state.Stack.Push(left <= right ? 1 : 0);
                            break;
                        }

                    case OpCode.UGt: {
                            var (left, right) = state.Stack.Pop<(uint left, uint right)>();
                            state.Stack.Push(left > right ? 1 : 0);
                            break;
                        }

                    case OpCode.UGe: {
                            var (left, right) = state.Stack.Pop<(uint left, uint right)>();
                            state.Stack.Push(left >= right ? 1 : 0);
                            break;
                        }

                    case OpCode.DAdd: {
                            var (left, right) = state.Stack.Pop<(double left, double right)>();
                            state.Stack.Push(left + right);
                            break;
                        }

                    case OpCode.DSub: {
                            var (left, right) = state.Stack.Pop<(double left, double right)>();
                            state.Stack.Push(left - right);
                            break;
                        }

                    case OpCode.DMul: {
                            var (left, right) = state.Stack.Pop<(double left, double right)>();
                            state.Stack.Push(left * right);
                            break;
                        }

                    case OpCode.DDiv: {
                            var (left, right) = state.Stack.Pop<(double left, double right)>();
                            state.Stack.Push(left / right);
                            break;
                        }

                    case OpCode.DNeg: {
                            var a = state.Stack.Pop<double>();
                            state.Stack.Push(-a);
                            break;
                        }

                    case OpCode.DEq: {
                            var (left, right) = state.Stack.Pop<(double left, double right)>();
                            state.Stack.Push(left == right ? 1 : 0);
                            break;
                        }

                    case OpCode.DNe: {
                            var (left, right) = state.Stack.Pop<(double left, double right)>();
                            state.Stack.Push(left != right ? 1 : 0);
                            break;
                        }

                    case OpCode.DLt: {
                            var (left, right) = state.Stack.Pop<(double left, double right)>();
                            state.Stack.Push(left < right ? 1 : 0);
                            break;
                        }

                    case OpCode.DLe: {
                            var (left, right) = state.Stack.Pop<(double left, double right)>();
                            state.Stack.Push(left <= right ? 1 : 0);
                            break;
                        }

                    case OpCode.DGt: {
                            var (left, right) = state.Stack.Pop<(double left, double right)>();
                            state.Stack.Push(left > right ? 1 : 0);
                            break;
                        }

                    case OpCode.DGe: {
                            var (left, right) = state.Stack.Pop<(double left, double right)>();
                            state.Stack.Push(left >= right ? 1 : 0);
                            break;
                        }

                    case OpCode.Narrow: {
                            int mode = ReadInt32(code, ref pc);
                            int v = state.Stack.PopInt();
                            state.Stack.Push(mode switch {
                                0 => v,
                                1 => (int)(uint)v,
                                2 => (int)(short)v,
                                3 => (int)(ushort)v,
                                4 => (int)(sbyte)v,
                                5 => (int)(byte)v,
                                _ => v,
                            });
                            break;
                        }

                    case OpCode.Jump:
                        pc = ReadInt32(code, ref pc);
                        break;

                    case OpCode.JumpIfFalse: {
                            int target = ReadInt32(code, ref pc);
                            int cond = state.Stack.PopInt();
                            if (cond == 0)
                                pc = target;
                            break;
                        }

                    case OpCode.Call: {
                            int funcIndex = ReadInt32(code, ref pc);
                            int argSlots = state.Stack.PopInt();
                            var entry = prog.Functions[funcIndex];
                            int retPC = pc;
                            int prevBase = state.FrameBase < 0 ? 0 : state.FrameBase;
                            int newBase = state.Stack.SP;
                            int totalSlots = FrameHeader.SlotCount + entry.LocalCount;
                            state.Stack.Reserve(totalSlots);
                            FrameHeader.Write(state.Stack.AsSpan(), newBase,
                                retPC, prevBase, argSlots, entry.RetBytes);
                            state.FrameBase = newBase;
                            pc = entry.PC;
                            break;
                        }

                    case OpCode.Return: {
                            if (state.FrameBase < 0)
                                break;
                            var hdr = FrameHeader.Read(state.Stack.AsSpan(), state.FrameBase);
                            int preArg = state.FrameBase - hdr.ArgSlots;
                            if (hdr.RetSlots > 0 && preArg >= 0) {
                                int retSrc = state.Stack.SP - hdr.RetSlots;
                                if (retSrc >= preArg) {
                                    state.Stack.CopyFrom(retSrc, preArg, hdr.RetSlots);
                                }
                            }
                            int finalSp = preArg + hdr.RetSlots;
                            if (finalSp >= 0 && finalSp <= state.Stack.SP)
                                state.Stack.TruncateTo(finalSp);
                            state.FrameBase = hdr.SavedPrevBase >= 0 ? hdr.SavedPrevBase : -1;
                            pc = hdr.RetPC;
                            break;
                        }

                    case OpCode.CallExternal: {
                            int siteIndex = ReadInt32(code, ref pc);
                            if ((uint)siteIndex >= (uint)prog.CallSites.Count || prog.CallSites[siteIndex] is null)
                                throw new InvalidOperationException(
                                    $"CALL_EXTERNAL: no target at site {siteIndex}");
                            prog.CallSites[siteIndex](state);
                            break;
                        }

                    case OpCode.Not: {
                            int val = state.Stack.PopInt();
                            state.Stack.Push(val == 0 ? 1 : 0);
                            break;
                        }

                    case OpCode.IsNull: {
                            int val = state.Stack.PopInt();
                            bool isNull = IsValidHeapHandle(state, val) && state.Heap.Get(val) is null;
                            state.Stack.Push(isNull ? 1 : 0);
                            break;
                        }

                    case OpCode.StoreArg: {
                            int paramIndex = ReadInt32(code, ref pc);
                            int value = state.Stack.PopInt();
                            if (state.FrameBase < 0)
                                throw new InvalidOperationException("StoreArg outside of function frame");
                            var hdr = FrameHeader.Read(state.Stack.AsSpan(), state.FrameBase);
                            int argStart = state.FrameBase - hdr.ArgSlots;
                            state.Stack.AsSpan()[argStart + paramIndex] = value;
                            break;
                        }

                    case OpCode.Throw: {
                            int exVal = state.Stack.PopInt();
                            var region = FindRegion(prog.ExceptionRegions, pc - 1);
                            if (region is not null) {
                                if (region.CatchStart >= 0) {
                                    state.Stack.Push(exVal);
                                    state.PendingExceptionValue = null;
                                    pc = region.CatchStart;
                                }
                                else if (region.FinallyStart is not null) {
                                    state.PendingExceptionValue = exVal;
                                    pc = region.FinallyStart.Value;
                                }
                            }
                            else {
                                throw new InvalidOperationException("Unhandled VM exception: " + exVal);
                            }
                            break;
                        }

                    case OpCode.EndFinally: {
                            if (state.PendingExceptionValue is not null) {
                                int exVal = state.PendingExceptionValue.Value;
                                state.PendingExceptionValue = null;
                                var region = FindRegion(prog.ExceptionRegions, pc - 1);
                                if (region is not null && region.CatchStart >= 0) {
                                    state.Stack.Push(exVal);
                                    pc = region.CatchStart;
                                }
                                else {
                                    throw new InvalidOperationException("Unhandled VM exception: " + exVal);
                                }
                            }
                            break;
                        }

                    case OpCode.Int: {
                            int vector = ReadInt32(code, ref pc);
                            state.SavedPC = pc;
                            if (vector == 0 || vector == 1) {
                                state.Status = InterpreterStatus.Suspended;
                            }
                            break;
                        }

                    case OpCode.Iret: {
                            if (state.SavedPC >= 0) {
                                pc = state.SavedPC;
                                state.SavedPC = -1;
                            }
                            break;
                        }

                    case OpCode.AllocateClosure: {
                            int funcIndex = ReadInt32(code, ref pc);
                            int captureCount = ReadInt32(code, ref pc);
                            var closure = new Closure(funcIndex, captureCount);
                            for (int i = captureCount - 1; i >= 0; i--)
                                closure.Captures[i] = state.Stack.PopInt();
                            int handle = state.Heap.Allocate(closure);
                            state.Stack.Push(handle);
#if DEBUG
                            if (trace is not null)
                                trace.WriteLine($"  → alloc Closure[#{handle}] func={funcIndex} captures={captureCount}");
#endif
                            break;
                        }

                    case OpCode.CallClosure: {
                            int argSlots = state.Stack.PopInt();
                            int closureHandle = state.Stack.AsSpan()[state.Stack.SP - argSlots];
                            var closure = state.Heap.Get(closureHandle) as Closure
                                ?? throw new InvalidOperationException("CallClosure target is not a Closure");
                            var entry = prog.Functions[closure.FuncIndex];
                            int retPC = pc;
                            int prevBase = state.FrameBase < 0 ? 0 : state.FrameBase;
                            int newBase = state.Stack.SP;
                            int totalSlots2 = FrameHeader.SlotCount + entry.LocalCount;
                            state.Stack.Reserve(totalSlots2);
                            FrameHeader.Write(state.Stack.AsSpan(), newBase,
                                retPC, prevBase, argSlots, entry.RetBytes);
                            state.FrameBase = newBase;
                            pc = entry.PC;
                            break;
                        }

                    case OpCode.LoadUpvalue: {
                            int upvalueIndex = ReadInt32(code, ref pc);
                            if (state.FrameBase < 0)
                                throw new InvalidOperationException("LoadUpvalue outside of function frame");
                            var hdr = FrameHeader.Read(state.Stack.AsSpan(), state.FrameBase);
                            int closureHandle = state.Stack.AsSpan()[state.FrameBase - hdr.ArgSlots];
                            var closure = state.Heap.Get(closureHandle) as Closure
                                ?? throw new InvalidOperationException("LoadUpvalue: no closure at arg 0");
                            int val = closure.Captures is not null && upvalueIndex < closure.Captures.Length && closure.Captures[upvalueIndex] is int iv
                                ? iv : 0;
                            state.Stack.Push(val);
                            break;
                        }

                    case OpCode.StoreUpvalue: {
                            int upvalueIndex = ReadInt32(code, ref pc);
                            int value = state.Stack.PopInt();
                            if (state.FrameBase < 0)
                                throw new InvalidOperationException("StoreUpvalue outside of function frame");
                            var hdr = FrameHeader.Read(state.Stack.AsSpan(), state.FrameBase);
                            int closureHandle = state.Stack.AsSpan()[state.FrameBase - hdr.ArgSlots];
                            var closure = state.Heap.Get(closureHandle) as Closure
                                ?? throw new InvalidOperationException("StoreUpvalue: no closure at arg 0");
                            if (closure.Captures is null || upvalueIndex >= closure.Captures.Length)
                                throw new InvalidOperationException($"StoreUpvalue: upvalue index {upvalueIndex} out of range");
                            closure.Captures[upvalueIndex] = value;
                            break;
                        }

                    case OpCode.StrConcat: {
                            int count = state.Stack.PopInt();
                            var parts = new string?[count];
                            for (int i = count - 1; i >= 0; i--) {
                                int handle = state.Stack.PopInt();
                                parts[i] = ResolveHeapValue(state, handle)?.ToString();
                            }
                            state.Stack.Push(state.Heap.Allocate(string.Concat(parts)));
                            break;
                        }

                    case OpCode.EnumeratorMoveNext: {
                            int handle = state.Stack.PopInt();
                            var enumerator = IsValidHeapHandle(state, handle)
                                && state.Heap.Get(handle) is object[] h ? h[0] as IEnumerator : null;
                            state.Stack.Push(enumerator?.MoveNext() ?? false ? 1 : 0);
                            break;
                        }

                    case OpCode.BitAnd: { var (l, r) = state.Stack.Pop<(int, int)>(); state.Stack.Push(l & r); break; }
                    case OpCode.BitOr: { var (l, r) = state.Stack.Pop<(int, int)>(); state.Stack.Push(l | r); break; }
                    case OpCode.BitXor: { var (l, r) = state.Stack.Pop<(int, int)>(); state.Stack.Push(l ^ r); break; }
                    case OpCode.BitNot: { int v = state.Stack.PopInt(); state.Stack.Push(~v); break; }
                    case OpCode.ShiftLeft: { var (l, r) = state.Stack.Pop<(int, int)>(); state.Stack.Push(l << r); break; }
                    case OpCode.ShiftRight: { var (l, r) = state.Stack.Pop<(int, int)>(); state.Stack.Push(l >> r); break; }
                    case OpCode.LBitAnd: { long r = state.Stack.Pop<long>(); long l = state.Stack.Pop<long>(); state.Stack.Push(l & r); break; }
                    case OpCode.LBitOr: { long r = state.Stack.Pop<long>(); long l = state.Stack.Pop<long>(); state.Stack.Push(l | r); break; }
                    case OpCode.LBitXor: { long r = state.Stack.Pop<long>(); long l = state.Stack.Pop<long>(); state.Stack.Push(l ^ r); break; }
                    case OpCode.LBitNot: { long v = state.Stack.Pop<long>(); state.Stack.Push(~v); break; }
                    case OpCode.LShiftLeft: { int r = state.Stack.PopInt(); long l = state.Stack.Pop<long>(); state.Stack.Push(l << r); break; }
                    case OpCode.LShiftRight: { int r = state.Stack.PopInt(); long l = state.Stack.Pop<long>(); state.Stack.Push(l >> r); break; }

                    default:
                        throw new InvalidOperationException($"Unimplemented op: {op}");
                }
            }

            state.PC = pc;

            if (state.IsSuspended) {
                var result = InterpreterResult.Suspend();
                state.SetLastResultWithoutChangingStatus(result);
                return result;
            }

            var finalResult = ExtractResult(state, prog);

            if (!state.IsComplete && !state.IsSuspended)
                state.Complete(finalResult);
            return finalResult;
        }
        catch (Exception ex) {
            state.PC = pc;
            var err = InterpreterResult.Throw(ex);
            state.Complete(err);
            return err;
        }
    }

    private static InterpreterResult ExtractResult(VmState state, Bytecode prog) {
        if (state.Stack.IsEmpty)
            return InterpreterResult.Void;

        var resultType = prog.ResultType;
        if (resultType is null || resultType == typeof(void)) {
            int val = state.Stack.PopInt();
            if (val > 0 && val < state.Heap.Count && state.Heap.Get(val) is not int)
                return InterpreterResult.FromValue(state.Heap.Get(val));
            return InterpreterResult.FromValue(val);
        }

        if (resultType == typeof(double) || resultType == typeof(float))
            return InterpreterResult.FromValue(state.Stack.Pop<double>());

        if (resultType == typeof(long) || resultType == typeof(ulong))
            return InterpreterResult.FromValue(state.Stack.Pop<long>());

        if (!resultType.IsPrimitive && !resultType.IsValueType) {
            int handle = state.Stack.PopInt();
            return IsValidHeapHandle(state, handle)
                ? InterpreterResult.FromValue(state.Heap.Get(handle))
                : InterpreterResult.FromValue(handle);
        }

        return InterpreterResult.FromValue(state.Stack.PopInt());
    }

    internal static object? ResolveHeapValue(VmState state, int raw) =>
        raw >= 0 && raw < state.Heap.Count ? state.Heap.Get(raw) : (object?)raw;

    internal static bool IsValidHeapHandle(VmState state, int handle) =>
        handle >= 0 && handle < state.Heap.Count;

    private static ExceptionRegion? FindRegion(IReadOnlyList<ExceptionRegion> regions, int pc) {
        for (int i = 0; i < regions.Count; i++) {
            var r = regions[i];
            if (pc >= r.TryStart && pc < r.TryEnd)
                return r;
        }
        return null;
    }

    private static string TruncateTrace(string s, int maxLen = 50) =>
        s.Length <= maxLen ? s : s[..(maxLen - 3)] + "...";

    private static int ReadInt32(byte[] code, ref int pc) {
        int val = code[pc] | (code[pc + 1] << 8) | (code[pc + 2] << 16) | (code[pc + 3] << 24);
        pc += 4;
        return val;
    }

    private static long ReadInt64(byte[] code, ref int pc) {
        long val = (long)code[pc] | ((long)code[pc + 1] << 8) | ((long)code[pc + 2] << 16) | ((long)code[pc + 3] << 24)
                 | ((long)code[pc + 4] << 32) | ((long)code[pc + 5] << 40) | ((long)code[pc + 6] << 48) | ((long)code[pc + 7] << 56);
        pc += 8;
        return val;
    }

    private static double ReadDouble(byte[] code, ref int pc) {
        long raw = (long)code[pc] | ((long)code[pc + 1] << 8) | ((long)code[pc + 2] << 16) | ((long)code[pc + 3] << 24)
                 | ((long)code[pc + 4] << 32) | ((long)code[pc + 5] << 40) | ((long)code[pc + 6] << 48) | ((long)code[pc + 7] << 56);
        pc += 8;
        return BitConverter.Int64BitsToDouble(raw);
    }

}