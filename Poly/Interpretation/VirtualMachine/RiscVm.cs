using System.Reflection;

using Poly.Interpretation.TreeWalking;
using Poly.Syntax.Analysis;

namespace Poly.Interpretation.VirtualMachine;

/// <summary>
/// Tiny RISC stack VM dispatch loop.
/// For incredibly simple programs (constants, basic arith on i64), this executes linear IR
/// produced by lowering or hand-crafted, leaves result on stack, and completes with InterpreterResult.
/// </summary>
internal static class RiscVm {
    public static InterpreterResult Execute(RiscState state) {
        if (state.Program == null) {
            state.Complete(InterpreterResult.Void);
            return InterpreterResult.Void;
        }

        var prog = state.Program;
        state.Status = InterpreterStatus.Running;
        state.PC = 0;

        const int MaxSteps = 100_000; // guard against bad jumps in basic tests
        int steps = 0;

        try {
            while (state.PC < prog.InstructionCount && !state.IsSuspended && !state.IsComplete) {
                if (++steps > MaxSteps)
                    throw new InvalidOperationException("Max instruction steps exceeded (possible infinite loop in IR)");

                var instr = prog.GetInstruction(state.PC);
                state.PC++;

                switch (instr.Op) {
                    // Data / constants
                    // LoadConst* are the (only) way a literal value enters the data stack from the IR text.
                    // All other "parameters" (jump targets, call targets, sizes, arg counts, handles for load/store)
                    // are explicitly pushed on the (operand) stack before the op, per the "everything via push/pop" model.
                    case RiscOp.LoadConst:
                    case RiscOp.LoadConstHandle:
                        state.Stack.Push(instr.Data);
                        break;

                    // Bulk load/store via signed handle (negative=stack absolute negated, positive=heap)
                    case RiscOp.LoadValue: {
                            // Expect caller pushed: size, then signed handle (handle on top per plan comment)
                            long h = state.Stack.Pop<long>();
                            long sz = state.Stack.Pop<long>();
                            int bc = (int)sz;
                            if (bc < 0) throw new InvalidOperationException("Negative load size");

                            if (h < 0) {
                                int src = RiscValueStack.ResolveStackHandle(h);
                                var srcSpan = state.Stack.AsSpan().Slice(src, bc);
                                var dest = state.Stack.ReserveBytes(bc);
                                srcSpan.CopyTo(dest);
                            }
                            else {
                                // Basic heap load as i64 (for simple programs)
                                var obj = state.Heap.Get((int)h);
                                long val = obj switch {
                                    long l => l,
                                    int i => i,
                                    _ => 0L
                                };
                                state.Stack.Push(val);
                            }
                            break;
                        }

                    case RiscOp.StoreValue: {
                            long h = state.Stack.Pop<long>();
                            long sz = state.Stack.Pop<long>();
                            int bc = (int)sz;
                            int cur = state.Stack.SP;
                            int srcOff = cur - bc;
                            if (srcOff < 0) throw new InvalidOperationException("Not enough bytes for StoreValue source");

                            var srcSpan = state.Stack.AsSpan().Slice(srcOff, bc);

                            if (h < 0) {
                                int dest = RiscValueStack.ResolveStackHandle(h);
                                // dest is in live lower segment (ancestor frame or current)
                                var destSpan = state.Stack.AsSpan().Slice(dest, bc);
                                srcSpan.CopyTo(destSpan);
                            }
                            else {
                                // Basic: store i64 to heap cell
                                long val = bc >= 8 ? System.Runtime.InteropServices.MemoryMarshal.Read<long>(srcSpan) : 0L;
                                state.Heap.Set((int)h, val);
                            }

                            state.Stack.DropBytes(bc);
                            break;
                        }

                    // Stack mgmt
                    case RiscOp.Dup: {
                            if (state.Stack.SP < 8) throw new InvalidOperationException("Dup underflow");
                            long top = state.Stack.Peek64(0);
                            state.Stack.Push(top);
                            break;
                        }

                    case RiscOp.Pop:
                        if (state.Stack.SP >= 8)
                            state.Stack.Pop<long>(); // discard
                        break;

                    // === Wide machine arithmetic (i64 / u64 / double only) ===
                    // Smaller types use these + explicit Narrow* (see below).

                    // I64
                    case RiscOp.Add when instr.Type is RiscType.i64 or RiscType.i32 or RiscType.i16 or RiscType.i8: {
                            var (a, b) = state.Stack.Pop2<long, long>();
                            state.Stack.Push(a + b);
                            break;
                        }
                    case RiscOp.Sub when instr.Type is RiscType.i64 or RiscType.i32 or RiscType.i16 or RiscType.i8: {
                            var (a, b) = state.Stack.Pop2<long, long>();
                            state.Stack.Push(a - b);
                            break;
                        }
                    case RiscOp.Mul when instr.Type is RiscType.i64 or RiscType.i32 or RiscType.i16 or RiscType.i8: {
                            var (a, b) = state.Stack.Pop2<long, long>();
                            state.Stack.Push(a * b);
                            break;
                        }
                    case RiscOp.Div when instr.Type is RiscType.i64 or RiscType.i32 or RiscType.i16 or RiscType.i8: {
                            var (a, b) = state.Stack.Pop2<long, long>();
                            if (b == 0) throw new DivideByZeroException("Division by zero in RISC Div");
                            state.Stack.Push(a / b);
                            break;
                        }
                    case RiscOp.Mod when instr.Type is RiscType.i64 or RiscType.i32 or RiscType.i16 or RiscType.i8: {
                            var (a, b) = state.Stack.Pop2<long, long>();
                            if (b == 0) throw new DivideByZeroException("Division by zero in RISC Mod");
                            state.Stack.Push(a % b);
                            break;
                        }
                    case RiscOp.Neg when instr.Type is RiscType.i64 or RiscType.i32 or RiscType.i16 or RiscType.i8: {
                            long v = state.Stack.Pop<long>();
                            state.Stack.Push(-v);
                            break;
                        }

                    // U64 (unsigned semantics for wraparound and division)
                    case RiscOp.Add when instr.Type is RiscType.u64 or RiscType.u32 or RiscType.u16 or RiscType.u8: {
                            var (a, b) = state.Stack.Pop2<ulong, ulong>();
                            state.Stack.Push(a + b);
                            break;
                        }
                    case RiscOp.Sub when instr.Type is RiscType.u64 or RiscType.u32 or RiscType.u16 or RiscType.u8: {
                            var (a, b) = state.Stack.Pop2<ulong, ulong>();
                            state.Stack.Push(a - b);
                            break;
                        }
                    case RiscOp.Mul when instr.Type is RiscType.u64 or RiscType.u32 or RiscType.u16 or RiscType.u8: {
                            var (a, b) = state.Stack.Pop2<ulong, ulong>();
                            state.Stack.Push(a * b);
                            break;
                        }
                    case RiscOp.Div when instr.Type is RiscType.u64 or RiscType.u32 or RiscType.u16 or RiscType.u8: {
                            var (a, b) = state.Stack.Pop2<ulong, ulong>();
                            if (b == 0) throw new DivideByZeroException("Division by zero in RISC Div");
                            state.Stack.Push(a / b);
                            break;
                        }
                    case RiscOp.Mod when instr.Type is RiscType.u64 or RiscType.u32 or RiscType.u16 or RiscType.u8: {
                            var (a, b) = state.Stack.Pop2<ulong, ulong>();
                            if (b == 0) throw new DivideByZeroException("Division by zero in RISC Mod");
                            state.Stack.Push(a % b);
                            break;
                        }

                    // F64
                    case RiscOp.Add when instr.Type == RiscType.f64: {
                            var (a, b) = state.Stack.Pop2<double, double>();
                            state.Stack.Push(a + b);
                            break;
                        }
                    case RiscOp.Sub when instr.Type == RiscType.f64: {
                            var (a, b) = state.Stack.Pop2<double, double>();
                            state.Stack.Push(a - b);
                            break;
                        }
                    case RiscOp.Mul when instr.Type == RiscType.f64: {
                            var (a, b) = state.Stack.Pop2<double, double>();
                            state.Stack.Push(a * b);
                            break;
                        }
                    case RiscOp.Div when instr.Type == RiscType.f64: {
                            var (a, b) = state.Stack.Pop2<double, double>();
                            state.Stack.Push(a / b);
                            break;
                        }
                    case RiscOp.Neg when instr.Type == RiscType.f64: {
                            var v = state.Stack.Pop<double>();
                            state.Stack.Push(-v);
                            break;
                        }


                    // F32
                    case RiscOp.Add when instr.Type == RiscType.f32: {
                            var (a, b) = state.Stack.Pop2<float, float>();
                            state.Stack.Push(a + b);
                            break;
                        }
                    case RiscOp.Sub when instr.Type == RiscType.f32: {
                            var (a, b) = state.Stack.Pop2<float, float>();
                            state.Stack.Push(a - b);
                            break;
                        }
                    case RiscOp.Mul when instr.Type == RiscType.f32: {
                            var (a, b) = state.Stack.Pop2<float, float>();
                            state.Stack.Push(a * b);
                            break;
                        }
                    case RiscOp.Div when instr.Type == RiscType.f32: {
                            var (a, b) = state.Stack.Pop2<float, float>();
                            state.Stack.Push(a / b);
                            break;
                        }
                    case RiscOp.Neg when instr.Type == RiscType.f32: {
                            float v = state.Stack.Pop<float>();
                            state.Stack.Push(-v);
                            break;
                        }

                    // === Comparisons (wide forms only) ===
                    // I64
                    case RiscOp.Eq when instr.Type is RiscType.i64 or RiscType.i32 or RiscType.i16 or RiscType.i8: {
                            var (a, b) = state.Stack.Pop2<long, long>();
                            state.Stack.Push(a == b ? 1 : 0);
                            break;
                        }
                    case RiscOp.Ne when instr.Type is RiscType.i64 or RiscType.i32 or RiscType.i16 or RiscType.i8: {
                            var (a, b) = state.Stack.Pop2<long, long>();
                            state.Stack.Push(a != b ? 1 : 0);
                            break;
                        }
                    case RiscOp.Lt when instr.Type is RiscType.i64 or RiscType.i32 or RiscType.i16 or RiscType.i8: {
                            var (a, b) = state.Stack.Pop2<long, long>();
                            state.Stack.Push(a < b ? 1 : 0);
                            break;
                        }
                    case RiscOp.Le when instr.Type is RiscType.i64 or RiscType.i32 or RiscType.i16 or RiscType.i8: {
                            var (a, b) = state.Stack.Pop2<long, long>();
                            state.Stack.Push(a <= b ? 1 : 0);
                            break;
                        }
                    case RiscOp.Gt when instr.Type is RiscType.i64 or RiscType.i32 or RiscType.i16 or RiscType.i8: {
                            var (a, b) = state.Stack.Pop2<long, long>();
                            state.Stack.Push(a > b ? 1 : 0);
                            break;
                        }
                    case RiscOp.Ge when instr.Type is RiscType.i64 or RiscType.i32 or RiscType.i16 or RiscType.i8: {
                            var (a, b) = state.Stack.Pop2<long, long>();
                            state.Stack.Push(a >= b ? 1 : 0);
                            break;
                        }

                    // U64
                    case RiscOp.Eq when instr.Type is RiscType.u64 or RiscType.u32 or RiscType.u16 or RiscType.u8: {
                            var (a, b) = state.Stack.Pop2<ulong, ulong>();
                            state.Stack.Push(a == b ? 1 : 0);
                            break;
                        }
                    case RiscOp.Ne when instr.Type is RiscType.u64 or RiscType.u32 or RiscType.u16 or RiscType.u8: {
                            var (a, b) = state.Stack.Pop2<ulong, ulong>();
                            state.Stack.Push(a != b ? 1 : 0);
                            break;
                        }
                    case RiscOp.Lt when instr.Type is RiscType.u64 or RiscType.u32 or RiscType.u16 or RiscType.u8: {
                            var (a, b) = state.Stack.Pop2<ulong, ulong>();
                            state.Stack.Push(a < b ? 1 : 0);
                            break;
                        }
                    case RiscOp.Le when instr.Type is RiscType.u64 or RiscType.u32 or RiscType.u16 or RiscType.u8: {
                            var (a, b) = state.Stack.Pop2<ulong, ulong>();
                            state.Stack.Push(a <= b ? 1 : 0);
                            break;
                        }
                    case RiscOp.Gt when instr.Type is RiscType.u64 or RiscType.u32 or RiscType.u16 or RiscType.u8: {
                            var (a, b) = state.Stack.Pop2<ulong, ulong>();
                            state.Stack.Push(a > b ? 1 : 0);
                            break;
                        }
                    case RiscOp.Ge when instr.Type is RiscType.u64 or RiscType.u32 or RiscType.u16 or RiscType.u8: {
                            var (a, b) = state.Stack.Pop2<ulong, ulong>();
                            state.Stack.Push(a >= b ? 1 : 0);
                            break;
                        }

                    // F64
                    case RiscOp.Eq when instr.Type is RiscType.f64: {
                            var (a, b) = state.Stack.Pop2<double, double>();
                            state.Stack.Push(a == b ? 1 : 0);
                            break;
                        }
                    case RiscOp.Ne when instr.Type is RiscType.f64: {
                            var (a, b) = state.Stack.Pop2<double, double>();
                            state.Stack.Push(a != b ? 1 : 0);
                            break;
                        }
                    case RiscOp.Lt when instr.Type is RiscType.f64: {
                            var (a, b) = state.Stack.Pop2<double, double>();
                            state.Stack.Push(a < b ? 1 : 0);
                            break;
                        }
                    case RiscOp.Le when instr.Type is RiscType.f64: {
                            var (a, b) = state.Stack.Pop2<double, double>();
                            state.Stack.Push(a <= b ? 1 : 0);
                            break;
                        }
                    case RiscOp.Gt when instr.Type is RiscType.f64: {
                            var (a, b) = state.Stack.Pop2<double, double>();
                            state.Stack.Push(a > b ? 1 : 0);
                            break;
                        }
                    case RiscOp.Ge when instr.Type is RiscType.f64: {
                            var (a, b) = state.Stack.Pop2<double, double>();
                            state.Stack.Push(a >= b ? 1 : 0);
                            break;
                        }

                    // F32
                    case RiscOp.Eq when instr.Type is RiscType.f32: {
                            var (a, b) = state.Stack.Pop2<float, float>();
                            state.Stack.Push(a == b ? 1 : 0);
                            break;
                        }
                    case RiscOp.Ne when instr.Type is RiscType.f32: {
                            var (a, b) = state.Stack.Pop2<float, float>();
                            state.Stack.Push(a != b ? 1 : 0);
                            break;
                        }
                    case RiscOp.Lt when instr.Type is RiscType.f32: {
                            var (a, b) = state.Stack.Pop2<float, float>();
                            state.Stack.Push(a < b ? 1 : 0);
                            break;
                        }
                    case RiscOp.Le when instr.Type is RiscType.f32: {
                            var (a, b) = state.Stack.Pop2<float, float>();
                            state.Stack.Push(a <= b ? 1 : 0);
                            break;
                        }
                    case RiscOp.Gt when instr.Type is RiscType.f32: {
                            var (a, b) = state.Stack.Pop2<float, float>();
                            state.Stack.Push(a > b ? 1 : 0);
                            break;
                        }
                    case RiscOp.Ge when instr.Type is RiscType.f32: {
                            var (a, b) = state.Stack.Pop2<float, float>();
                            state.Stack.Push(a >= b ? 1 : 0);
                            break;
                        }

                    // === Explicit narrowing to smaller types ===
                    case RiscOp.Narrow when instr.Type is RiscType.i32: {
                            long v = state.Stack.Pop<long>();
                            state.Stack.Push((int)v);   // sign-extends in the 64-bit slot
                            break;
                        }
                    case RiscOp.Narrow when instr.Type is RiscType.u32: {
                            long v = state.Stack.Pop<long>();
                            state.Stack.Push((uint)v);  // high bits zeroed
                            break;
                        }
                    case RiscOp.Narrow when instr.Type is RiscType.i16: {
                            long v = state.Stack.Pop<long>();
                            state.Stack.Push((short)v);
                            break;
                        }
                    case RiscOp.Narrow when instr.Type is RiscType.u16: {
                            long v = state.Stack.Pop<long>();
                            state.Stack.Push((ushort)v);
                            break;
                        }
                    case RiscOp.Narrow when instr.Type is RiscType.i8: {
                            long v = state.Stack.Pop<long>();
                            state.Stack.Push((sbyte)v);
                            break;
                        }
                    case RiscOp.Narrow when instr.Type is RiscType.u8: {
                            var v = state.Stack.Pop<long>();
                            state.Stack.Push((byte)v);
                            break;
                        }
                    case RiscOp.Narrow when instr.Type is RiscType.f32: {
                            var d = state.Stack.Pop<double>();
                            state.Stack.Push((float)d);
                            break;
                        }

                    // Control flow
                    // Targets are embedded in the instruction fields (Data for target) — no stack-based fallback.
                    case RiscOp.Jump: {
                            state.PC = (int)instr.Data;
                            break;
                        }

                    case RiscOp.JumpIfFalse: {
                            var cond = state.Stack.Pop<long>();
                            if (cond == 0)
                                state.PC = (int)instr.Data;
                            break;
                        }

                    // Calls / frames (segments on the single stack)
                    // Parameter fields (Source=argBytes, Data=target) are always read from the instruction.
                    // Dynamic args (including by-ref stack handles) are still pushed as argData before the op.
                    case RiscOp.Call: {
                            long argBytes = instr.Source;
                            long target = instr.Data;
                            int retPC = state.PC;
                            int prevBase = state.FrameBases.Count == 0 ? 0 : state.FrameBases[^1];
                            int callerPersp = prevBase;

                            int newBase = state.Stack.SP;
                            var hspan = state.Stack.ReserveBytes(RiscFrameHeader.Size);
                            RiscFrameHeader.WriteHeader(hspan, retPC, prevBase, callerPersp, (int)argBytes);
                            state.FrameBases.Add(newBase);

                            // After the header, the callee IR can immediately ReserveBytes(N) for its locals
                            // (or a future ALLOC_LOCALS op). The negative handles for those locals are
                            // absolute offsets computed at "issuance" time relative to the frame base.
                            // This is the standard pattern — lowering controls the reservation.
                            state.PC = (int)target;
                            break;
                        }

                    case RiscOp.Return: {
                            if (state.FrameBases.Count == 0) {
                                // top-level or unbalanced; let end-of-loop extraction handle value if any
                                break;
                            }

                            int curBase = state.FrameBases[^1];
                            var hro = state.Stack.AsSpan().Slice(curBase, RiscFrameHeader.Size);
                            var (retPC, prevB, cPersp, argBFromHeader) = RiscFrameHeader.ReadHeaderEx(hro);

                            long argSz = instr.Source;
                            long retSz = instr.Data;

                            int argSzI = (int)argSz != 0 ? (int)argSz : argBFromHeader;
                            int retSzI = (int)retSz;

                            int preArg = curBase - argSzI;

                            if (retSzI > 0) {
                                int retSrc = state.Stack.SP - retSzI;
                                if (retSrc >= 0) {
                                    var retData = state.Stack.AsSpan().Slice(retSrc, retSzI);
                                    // copy return value down to the caller's post-arg position
                                    var dest = state.Stack.AsSpan().Slice(preArg, retSzI);
                                    retData.CopyTo(dest);
                                }
                            }

                            int finalSP = preArg + retSzI;
                            state.Stack.TruncateTo(finalSP);
                            state.FrameBases.RemoveAt(state.FrameBases.Count - 1);
                            state.PC = retPC;
                            break;
                        }

                    case RiscOp.CallExternal: {
                            // Instruction fields:
                            //   Data   = siteIndex into state.CallTargets
                            //   Source = argByteCount (total bytes of arg data on stack)
                            //   Dest   = hasRet (0 or 1)
                            //
                            // Stack before CALL_EXTERNAL (top → bottom):
                            //   [argData...]  (signed handles: + = heap index, - = stack offset)
                            //
                            // The arg data is already pushed by preceding Emit() calls for arg nodes.
                            // No protocol values are popped from the stack — everything comes from the instruction.
                            int siteIndex = (int)instr.Data;
                            int argB = (int)instr.Source;
                            bool hasRet = instr.Dest != 0;

                            var target = (uint)siteIndex < (uint)state.CallTargets.Count
                                ? state.CallTargets[siteIndex]
                                : null;

                            if (target is null)
                                throw new InvalidOperationException(
                                    $"CALL_EXTERNAL: no target at site {siteIndex} (CallTargets.Count={state.CallTargets.Count})");

                            // Marshal args from the stack byte region
                            int argCount = argB / 8;
                            var args = argCount > 0 ? new object?[argCount] : [];

                            // Arg data is between SP - argB and SP
                            int argDataStart = state.Stack.SP - argB;
                            for (int i = 0; i < argCount; i++) {
                                int off = argDataStart + i * 8;
                                long handle = System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(
                                    state.Stack.AsSpan().Slice(off, 8));

                                if (handle >= 0 && handle < state.Heap.Count) {
                                    args[i] = state.Heap.Get((int)handle);
                                }
                                else if (handle < 0) {
                                    int absOff = RiscValueStack.ResolveStackHandle(handle);
                                    args[i] = System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(
                                        state.Stack.AsSpan().Slice(absOff, 8));
                                }
                                else {
                                    args[i] = handle;
                                }
                            }

                            object? result;
                            if (target is MethodInfo mi) {
                                var miParams = mi.GetParameters();
                                var alignedArgs = AlignArgTypes(args, miParams);
                                result = mi.IsStatic
                                    ? mi.Invoke(null, alignedArgs)
                                    : mi.Invoke(args[0], alignedArgs[1..]);
                            }
                            else if (target is Delegate del) {
                                result = del.DynamicInvoke(args);
                            }
                            else {
                                throw new InvalidOperationException(
                                    $"CALL_EXTERNAL: target at site {siteIndex} is {target.GetType().Name}, expected MethodInfo or Delegate");
                            }

                            // Drop arg bytes from stack
                            if (argB > 0)
                                state.Stack.DropBytes(argB);

                            if (hasRet) {
                                long retVal = result switch {
                                    long l => l,
                                    int i => i,
                                    short s => s,
                                    byte b => b,
                                    bool b => b ? 1L : 0L,
                                    null => 0L,
                                    _ => 0L
                                };
                                state.Stack.Push(retVal);
                            }
                            break;
                        }

                    // Suspension (neurosymbolic)
                    case RiscOp.Suspend:
                        state.Status = InterpreterStatus.Suspended;
                        break;

                    case RiscOp.Nop:
                        break;

                    default:
                        throw new InvalidOperationException($"Unimplemented RISC op in core VM: {instr.Op} (Data={instr.Data})");
                }
            }

            if (state.IsSuspended) {
                // Basic handling of SUSPEND instruction: keep Suspended status (core point of the op).
                // Full version will also capture raw buffer + heap + frameBases + pc + source map.
                var susp = InterpreterResult.FromValue("SUSPENDED");
                state.SetLastResultWithoutChangingStatus(susp);
                return susp;
            }

            // Implicit end-of-program / halt:
            // When we fall off the end of the instruction list (PC >= count) without an explicit
            // Return or Suspend, extract the top-of-stack value (if any) as the program result.
            // This matches "void is implicitly resultless" — if nothing was left on the stack,
            // we produce Void. Lowering is responsible for ensuring a clean stack shape for
            // void programs (or using explicit POPs where needed).
            object? resultValue = null;
            if (state.Stack.SP >= 8) {
                var top = state.Stack.Pop<long>();
                resultValue = top;
            }

            var finalResult = resultValue is not null
                ? InterpreterResult.FromValue(resultValue)
                : InterpreterResult.Void;

            if (!state.IsComplete && !state.IsSuspended) {
                state.Complete(finalResult);
            }
            return finalResult;
        }
        catch (Exception ex) {
            var err = InterpreterResult.Throw(ex);
            state.Complete(err);
            return err;
        }
    }

    // Align raw argument values to MethodInfo parameter types so Invoke doesn't throw
    // on primitive type mismatches (e.g. long arg → int param).
    private static object?[] AlignArgTypes(object?[] rawArgs, ParameterInfo[] paramInfos) {
        if (rawArgs.Length == 0 || paramInfos.Length == 0)
            return rawArgs;

        var aligned = new object?[rawArgs.Length];
        int count = Math.Min(rawArgs.Length, paramInfos.Length);
        for (int i = 0; i < count; i++) {
            var val = rawArgs[i];
            var targetType = paramInfos[i].ParameterType;
            if (val is long l && targetType != typeof(long)) {
                try { aligned[i] = System.Convert.ChangeType(l, targetType); }
                catch { aligned[i] = val; }
            }
            else if (val is int iv && targetType != typeof(int)) {
                try { aligned[i] = System.Convert.ChangeType(iv, targetType); }
                catch { aligned[i] = val; }
            }
            else {
                aligned[i] = val;
            }
        }
        for (int i = count; i < rawArgs.Length; i++)
            aligned[i] = rawArgs[i];

        return aligned;
    }
}