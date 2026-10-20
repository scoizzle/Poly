# VM ABI Reference

This document describes the Poly Virtual Machine's Application Binary Interface (ABI)
in detail: the call frame layout, register model, value representation, and calling
convention.

## Overview

The Poly VM ABI is designed for direct AST-to-delegate compilation via
`DirectVmAbiEmitter`. There is no bytecode interpreter or intermediate primitive
representation — the ABI is the contract between the compiler (emitter) and the
runtime (`VmState`).

## Word

The fundamental storage unit is `Word` — a wrapper around `long`:

- **Positive values**: Stack scalars (integers, floating-point bit patterns, etc.)
- **Negative values**: Heap handles (`Word.IsHandle == true`)
- **Zero**: Null/false sentinel

Implicit conversions exist between `Word` and `int`/`long`. The `int` conversion
clamps to `int.MaxValue` for out-of-range values.

## Value Stack

`ValueStack` stores `long` values in a pooled array (`ArrayPool<long>.Shared`):

| Operation | Description |
|-----------|-------------|
| `Push(value)` | Push a value; auto-grows by doubling |
| `Pop()` | Pop and return the top value |
| `Drop(n)` | Discard top `n` values |
| `Reserve(n)` | Reserve `n` slots (no write) |
| `RawSlots[i]` | Direct array access (unchecked) |

The stack grows automatically when full. Disposal returns the buffer to the pool.

## Heap

Reference-type values are stored in a contiguous `object?[]` array indexed by handle:

| Handle | Meaning |
|--------|---------|
| 0 | Null/falsy sentinel (never allocated to a live object) |
| 1..N | Valid heap handles |

The heap uses a free-list to recycle handles when objects are set to null.

## Call Frame Layout

The layout is defined once in `VmCall` (`Poly/Interpretation/Vm/VmCall.cs`).
A frame starts at `callSp = state.Stack.StackPointer`:

```
Slot[callSp + 0]:  previous FP (caller's FramePos)
Slot[callSp + 1]:  saved SP (callSp)
Slot[callSp + 2]:  arg0            ← FramePos (FP)
Slot[callSp + 3]:  arg1
...
Slot[FP + n]:      result          (written by the callee)
```

`VmCall.FramePointer(callSp)` is `callSp + 2`. `FramePos` on `VmState` is that
first argument slot and persists across suspend/resume. A parameterless callee
still reserves one argument slot (`VmCall.ArgumentSlots`) so its result has a
slot to write to.

### Invocation

`VmCall.Invoke(state, function, args, closureHandle)`:

1. reads `callSp` from `state.Stack.StackPointer`
2. checks the stack has room for `VmCall.Extent(args.Length)` slots
3. writes the header, then the argument slots
4. sets `FramePos = FramePointer(callSp)` and the closure handle
5. runs the callee delegate
6. returns `slots[FramePos]`
7. in `finally`, restores `FramePos`, `ClosureHandle`, and `SP = callSp`

### Callee

1. a parameter resolves at `FramePos + index`; an instance frame's receiver
   occupies `FramePos` and user parameters follow it
2. `return` writes `slots[FramePos]` and sets `SP = FramePos + 1`
3. a void callee writes no result; the caller does not read the slot

Stored-closure heap layout: `object[]` at the handle, `[0] = lambda index (boxed long)`, `[1..] = `long[1]` cells. Function-table bodies read and write `cell[0]`. The enclosing frame stores a heap handle to the same cell in the captured variable's slot. Capture lists come from analysis (`LambdaCaptureMetadata`). `Invoke(Variable)` is typed as the lambda body's value, so result unwrap reads a scalar word by the body's CLR type (a `bool` is 0/non-zero) rather than treating ABI `1` as heap handle 1. `VariableLayout.IsUpvalueCell` tells `VmDebugger.GetLocals` to present `cell[0]`. Invoke fails closed if an upvalue slot is not a `long[1]`.

## Ring Register Model

The emitter uses ring registers during compilation — local variables `_r0.._rN`
that hold temporary values during expression evaluation:

| Property | Value |
|----------|-------|
| Default slots | 8 |
| Max slots | 32 |
| Allocation | Inline during AST walk (no global pre-pass) |
| JIT behavior | CLR JIT can enregister ring locals |

The ring depth equals the maximum expression nesting depth at any point in the
function. Each µop reads its inputs from and writes its output to ring slots
indexed by eval-stack position.

## Register File (`VmState.Registers`)

A flat `long[]` array used for miscellaneous runtime state:

- Allocated lazily (256 slots by default)
- Set before the program delegate runs
- Used for loop counters, temporary values, etc.

## Compilation Modes

| Mode | DebugHook | DebugInterrupt | PC Tracking | Use Case |
|------|-----------|----------------|-------------|----------|
| `Normal` | Active | Active | Active | Development, debugging, stepping |
| `NoDebug` | Omitted | Omitted | Omitted | Production, benchmarks |

In `NoDebug` mode, the emitted expression tree omits all debug/trace checks,
producing a smaller and faster delegate.

## µop Tracing

When `VmState.Trace` is non-null, the compiled delegate calls
`VmTrace.LogUop(pc, text, sp, fb, state)` before each µop:

```
   0 add                        depth=0   fb=0
   1 store local0               depth=1   fb=0
   ...
```

Overhead is ~1 ns when `state.Trace` is null (default). Enabled regardless of
compilation mode — the trace check is a single null branch.

## Debug Hooks

| Hook | Call Frequency | Arguments | Overhead When Null |
|------|---------------|-----------|-------------------|
| `DebugInterrupt` | Every µop (Normal) | `VmState` | Single null check |
| `DebugHook` | Every AST node (Normal) | `Node`, `ReadOnlySpan<long>`, `Heap` | Single null check |

`DebugHook` is preferred — it provides symbolic AST context without exposing
the full state, and the locals span is built at compile time.

## Value Marshalling

`RingCodec` handles conversion between VM internal representation and
CLR types (`FromClr`/`ToClr` at compile time, `ToRing`/`Cast`/`TypeAs`/
`UnboxNullable` at runtime):

| CLR Type | VM Representation | Direction |
|----------|-------------------|-----------|
| `int`, `long`, `float`, `double` | Stack scalar (`long` bit pattern) | Both |
| `bool` | 0 = false, non-0 = true | Both |
| `short`, `byte` | Stack scalar (converted to/from `long`) | Both |
| Reference types | Heap handle (`int` index) | Both |
| Value types | Stack scalar or heap (depends on size) | Both |
