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

Each function activation occupies a contiguous region on the value stack:

```
                          Low addresses (earlier pushes)
  ┌──────────────────────────────────────────────┐
  │           Caller's frame                     │
  │  [caller locals ... caller temporaries]      │
  ├──────────────────────────────────────────────┤
  │           Argument slots                     │
  │  arg0 (highest address, first pushed)        │
  │  arg1                                       │
  │  ...                                        │
  │  argN-1 (lowest address, last pushed)        │
  ├──────────────────────────────────────────────┤ ← savedSP
  │           Frame header (2 words)             │
  │  previousFP (caller's frame pointer)         │
  │  savedSP (SP before header push)             │
  ├──────────────────────────────────────────────┤ ← savedSP + 2
  │           Local slots                        │
  │  local0                                     │
  │  local1                                     │
  │  ...                                        │
  │  localM-1                                   │
  └──────────────────────────────────────────────┘ ← SP
                          High addresses (later pushes)
```

### Frame Header Fields

| Field | Size | Description |
|-------|------|-------------|
| `previousFP` | 1 word | Caller's frame pointer (-1 for root frame) |
| `savedSP` | 1 word | Stack pointer value just before this header was pushed |

Argument and local counts are **not** stored on the stack — they are known at
compile time and attached to the `CallStackFrame` view.

### Frame Access

The `CallStack` class provides typed accessors:

| Method | Description |
|--------|-------------|
| `GetLocal(frame, i)` | Reference to local variable `i` |
| `GetArgument(frame, i)` | Reference to argument `i` |
| `GetLocals(frame)` | Span over all locals |
| `GetArguments(frame)` | Span over all arguments |
| `AllocateFrame(pfp, ssp, argc, localc)` | Push new frame |
| `DeallocateFrame(frame)` | Pop frame, restore SP |

### Frame Base
- `FrameBase = savedSP` (where the frame header starts)
- `FrameBase` sentinel `-1` = "no active frame" (top-level execution)
- `FramePos` on `VmState` persists the frame position across suspend/resume

## Calling Convention

### Caller Responsibilities
1. Evaluate arguments left-to-right, pushing each onto the stack
2. Advance SP past the argument slots
3. Set `state.ClosureHandle` for closure calls
4. Invoke the callee delegate (for external methods or compiled function bodies)

Stored-closure heap layout: `object[]` at the handle, `[0] = lambda index (boxed long)`, `[1..] = `long[1]` cells. Function-table bodies read and write `cell[0]`. The enclosing frame stores a heap handle to the same cell in the captured variable's slot. Capture lists come from analysis (`LambdaCaptureMetadata`). `Invoke(Variable)` is typed as the lambda body's value kind so result unwrap uses `Bool` / `StackScalar` rather than treating ABI `1` as heap handle 1. `VariableLayout.IsUpvalueCell` tells `VmDebugger.GetLocals` to present `cell[0]`. Invoke fails closed if an upvalue slot is not a `long[1]`.

### Callee Responsibilities
1. Allocate frame header (push previousFP + savedSP)
2. Reserve local slots
3. Execute function body
4. Write result to `FrameBase[0]` (first local/arg slot)
5. Set `SP = FrameBase + 1` (leaving result on stack)
6. Jump to exit or return to caller

### Return Convention
- Result is in `Slot[FrameBase]` with `SP = FrameBase + 1`
- For void functions, the caller does not read the result
- Cross-function returns (restoring caller PC/FP from metadata) are planned
  for when nested calls across compiled functions are required

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

`VmValueMarshaller` handles conversion between VM internal representation and
CLR types:

| CLR Type | VM Representation | Direction |
|----------|-------------------|-----------|
| `int`, `long`, `float`, `double` | Stack scalar (`long` bit pattern) | Both |
| `bool` | 0 = false, non-0 = true | Both |
| `short`, `byte` | Stack scalar (converted to/from `long`) | Both |
| Reference types | Heap handle (`int` index) | Both |
| Value types | Stack scalar or heap (depends on size) | Both |
