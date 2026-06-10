# VM JIT Plan: Jitterpreter

## Goal

Reduce VM instruction execution from ~43 ns/inst to **~1.5 ns/inst** for the common case, while preserving full debugger support and correctness for all programs.

## Architecture

```
Vm.Execute(state):
    if (!state.DebuggerIsActive && prog.Compiled is { } compiled)
        compiled(state);          // JIT path: ~1.5 ns/inst
    else
        Interpret(state, prog);   // Interpreter: ~7 ns/inst (stack-inlined)
```

**JIT path:** A `BytecodeJit` pass reads `prog.Code` during the first `Vm.Execute` call and emits an `Expression<Action<VmState>>`. The JIT compiles this to native code — no dispatch loop, no switch, no indirect branches. The entire program is a single method where every instruction is a direct array access and ALU operation.

**Interpreter path:** The stack-inlined interpreter with fast/slow path split. Used when breakpoints are active or when the JIT doesn't yet support a specific opcode.

**Debugger:** Sets `state.DebuggerIsActive = true` when breakpoints exist, skipping the JIT delegate entirely. `VmDebugger` patches bytecodes with `Int` opcodes as it does today. The interpreter reads patched bytecodes, suspends on `Int`, resumes via `Iret`. Zero JIT changes needed.

## Quick Win: Phase 1

**Coverage:** 16 opcodes + `Call`/`Return` for functions. Enough for Fibonacci, Factorial, GCD, SumSquares.

| Category | Opcodes |
|----------|---------|
| Stack | `Nop`, `Dup`, `Pop` |
| Push | `PushInt`, `PushLong`, `PushDouble`, `LoadConst` |
| Locals | `LoadArg`, `LoadLocal`, `StoreLocal` |
| Arithmetic | `Add`, `Sub`, `Mul`, `Div`, `Mod`, `Neg` |
| Comparison | `Eq`, `Ne`, `Lt`, `Le`, `Gt`, `Ge` |
| Boolean | `Not` |
| Control flow | `Jump`, `JumpIfFalse` |
| Functions | `Call`, `Return` |
| Closures | `AllocateClosure`, `CallClosure`, `LoadUpvalue`, `StoreUpvalue` |

## Implementation Order

| Phase | What | Time |
|-------|------|------|
| 0a | Drop step counter (`MaxSteps`) | 5 min |
| 0b | Stack inlining in interpreter | 90 min |
| 0c | Fast/slow path split | 15 min |
| 0d | Constant pre-load cache | 10 min |
| 1 | BytecodeJIT quick win | 4-5 hrs |
| 2+ | Extended opcode coverage | Incremental |

## Debugger

`VmDebugger` sets `state.DebuggerIsActive = true` → interpreter path. No deoptimization, no JIT awareness. The interpreter handles all programs correctly — the JIT is purely an acceleration layer.

## Status (June 2026)

### Implemented
- **Stack inlining** — All Push/Pop → direct `slots[sp]` access in Vm.cs
- **Step counter removed** — `MaxSteps` variable and check deleted
- **Constant cache** — Heap not cleared on `Reset()` when program unchanged
- **CachedArgSlots** — FrameHeader read eliminated from LoadArg/StoreArg/LoadUpvalue/StoreUpvalue
- **BytecodeJIT** — Expression-tree JIT for 20+ opcodes (arithmetic, control flow, locals)
- **JitHelpers.Call** — Set up frame + RunInterpreter for function bodies
- **Function body JIT** — Lambda bodies compiled via LinqExpressionGenerator at lowering time, stored as CallSiteDelegates. JitHelpers.Call prefers compiled delegate over frame setup + RunInterpreter
- **VmDebugger.DebuggerIsActive** — JIT skipped when debugger has breakpoints. Interpreter handles `Int`/`Iret`/`Throw`/suspension naturally

### Performance
- All 1242 tests pass in **~8-10s** (was 11s before optimizations)
- **Vm_Poly**: **42.3 ns** (was 71.6 ns) — **41% faster**
- **Vm_PolyParam**: **43.2 ns** (was ~90 ns) — **52% faster**
- Pure arithmetic programs: fully JIT-compiled (expression tree, no dispatch loop)
- Program with function calls: main body JIT'd, function bodies via LinqExpressionGenerator+JitHelpers or RunInterpreter
- Programs with unsupported ops: interpreter fallback (stack-inlined, ~7 ns/inst)
- Debugging: interpreter handles all Int/Iret/Throw/breakpoints via `DebuggerIsActive` flag
