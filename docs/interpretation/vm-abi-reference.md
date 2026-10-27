# VM ABI Reference

This document describes the Poly Virtual Machine's Application Binary Interface (ABI)
in detail: the call frame layout, local pool, value representation, and calling
convention.

## Overview

The Poly VM ABI is designed for direct AST-to-delegate compilation via
`AbiEmitter`. There is no bytecode interpreter or intermediate primitive
representation — the ABI is the contract between the compiler (emitter) and the
runtime (`VmState`).

## Value Stack

`VmValueStack` stores `long` values in a pooled array (`ArrayPool<long>.Shared`):

| Operation | Description |
|-----------|-------------|
| `Push(value)` | Push a value; auto-grows by doubling |
| `Pop()` | Pop and return the top value |
| `Drop(n)` | Discard top `n` values |
| `RawSlots[i]` | Direct array access (unchecked) |

The stack grows automatically when full. Disposal returns the buffer to the pool.

## VmManagedObjectHeap

Reference-type values are stored in a contiguous `object?[]` array indexed by handle:

| Handle | Meaning |
|--------|---------|
| 0 | Null/falsy sentinel (never allocated to a live object) |
| 1..N | Valid heap handles |

A handle is stable for the heap's lifetime. A stored null is a value, not a free:
`Set` and `UnsafeSet` keep the handle live, so a store can never hand a slot to a later
allocation that another holder still points at. `Recycle(handle)` is the one call that
releases a handle — the caller proves the object can never be observed again — and it
fails closed on a double recycle (`InvalidOperationException`), on handle 0, and on a
handle the heap never issued. Its backing array is rented from `ArrayPool<object?>.Shared`
and returned cleared on dispose, so short-lived executions do not allocate a heap buffer.

A `typeof` value is seeded, not allocated per evaluation: one entry per distinct type is
allocated at program entry and held in a `_typeN` local, so a `typeof` compiles to a local
read. Before that, a `typeof` inside a loop allocated a fresh handle for the same `Type` on
every iteration, and no type object was ever recycled.

A promoted array's element accesses include `StridedSetBits`'s array operand: the intrinsic
indexes the buffer and never hands it on, so it counts as a receiver for both promotion and
the scope-exit recycle. `bits[j >> 6] |= mask` therefore compiles against the cached local, not
through `heap.UnsafeGet`.

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
3. a callee always leaves a result word: a body that yields none writes `0L`, so
   a caller never reads a slot the callee left untouched

### Result

Every invocation path produces its ring outcome through `AbiEmitter.EmitResult(call, returnType)`
— the one convention:

| Call | Ring outcome |
|------|--------------|
| `void` | `0L`, so the slot the caller reserved is always written |
| inline scalar, `bool`, enum | the widened word (`bool` is `1`/`0`) |
| `float` / `double` | the IEEE bit pattern |
| reference, non-inline value type, unresolved return type | heap handle (`RingCodec.FromClr`) |

A callee that is a VM function (an AST method or a stored closure) is entered through
`VmCall.Invoke`, which returns `slots[FramePointer]` — already the ring word — so that caller
only moves the word into its own slot and never re-normalises it. The reverse direction, a
bridged lambda returning to a CLR delegate or `Expression<D>`, is the single decode
`RingCodec.ToClr`.

Stored-closure heap layout: `object[]` at the handle, `[0] = lambda index (boxed long)`, `[1..] = `long[1]` cells. Function-table bodies read and write `cell[0]`. The enclosing frame stores a heap handle to the same cell in the captured variable's slot. Capture lists come from analysis (`LambdaCaptureMetadata`). `Invoke(Variable)` is typed as the lambda body's value, so result unwrap reads a scalar word by the body's CLR type (a `bool` is 0/non-zero) rather than treating ABI `1` as heap handle 1. `VariableLayout.IsUpvalueCell` tells `VmDebugger.GetLocals` to present `cell[0]`. Invoke fails closed if an upvalue slot is not a `long[1]`.

## Local pool

The emitter keeps hot values in one `LocalPool`. Variable leases, expression temps,
and named helpers (`_coal`, `_shift`, `_callSp`, `_callerFp`) share that cursor.
A local's name is its `LocalType`:

| Property | Value |
|----------|-------|
| Allocation | Inline during the AST walk, on demand (no pre-pass) |
| High-water | The deepest statement in a block, not the block's total |
| Release | `EmitScope` disposal rewinds anything that was not yielded; a block restarts each statement at its entry depth |
| JIT behavior | Pool locals are ordinary CLR locals, so the JIT can enregister them |

### A slot carries a `LocalType`

A pool local is keyed by **(index, `LocalType`)**, not by index alone
(`LocalPool.Ring` / `Context.RingVar`). Ring depth rewinds after every statement, so
absolute index 3 holds a `double` in one statement and a `long` in the next; one local
per index would have to be assigned a value of the wrong type, and `Expression.Assign`
rejects a mismatched type rather than coercing. Distinct locals per `LocalType` keep
every assignment type-correct.

| Value | Pool local | Contents |
|-------|-----------|----------|
| `double` | `_dN` | the value itself |
| `float` | `_fN` | the value itself, 32-bit |
| `int` | `_iN` | the value itself |
| `Nullable<T>` | `_nN_T` | the nullable value |
| every other scalar, a handle, or an unresolved representation | `_rN` | the `long` word |

An IEEE arm stores into and reads from the local of its own type, so an arithmetic
chain — `zx*zx + zy*zy` — contains no `BitConverter` call between its operands. The
**word is the fallback**: a representation analysis cannot resolve is not an error.
An `int` keeps its own local; every other integer stays a word, so the local count
does not grow.

### A variable is a pool lease

A variable is a `Local` leased from the same pool, at most 32 live
(`MaxVariableLeases`). A write assigns the lease. There is no copy on write. The
33rd simultaneous variable has no free lease and lives only in its frame slot
(`_slots[_fp + n]`).

A lease uses the same `LocalType` names as a temp. A captured (cell-backed) lease
holds the capture cell's heap handle, not `cell[0]`, because the cell is where the
value lives; typing the lease by the variable's value would coerce the handle.

A lease index is released when its scope ends and reused by the next scope's
variable, which may have a different `LocalType`. Index 0 can back a `double` in
one block and a word in the next. Leases are keyed by **(index, `LocalType`)**,
so two variables never alias one `ParameterExpression` at two types.
`RegisterIndex_ReusedAcrossRepresentations_DoesNotAlias` is the test.

Reading a `double` variable into a pool local of the same type is a copy, and
assigning one is a copy: no conversion per access. The numeric conversion between
width classes (an integer assigned to a `double` widens, a `double` assigned to an
integer truncates) happens once, at the assignment, in `AbiEmitter.AssignedValue`.

**Frame slots are observation copies.** A lease is stored into `_slots[_fp + n]`
only at an observation point:

| Point | When |
|-------|------|
| Block exit | Inside the hook guard, and only while a hook is attached |
| Catch exit | Inside the hook guard, after a catch body, and only while a hook is attached |
| Debug hook | Inside the hook guard, before the call, and only while a hook is attached |
| Suspend | Once per suspend point. Resume re-enters the method and CLR locals start at zero |

The suspend store is the frame encoding of the local (`SlotLocation.Frame.Write`,
which is `LocalType.Encode`; a nullable boxes). The reload is `LocalType.FromWord`,
and it is the first expression after the resume label, before the suspended flag
is cleared. The spill set is each live local that dies on resume and is read after
it: variable leases, enclosing temps still needed, and helpers assigned before the
suspend and read after it (`LocalPool.LiveHelpers`). A binding whose only home is
already a frame word is not spilled. A cell spills its handle once. A binding is
never spilled twice.

`Value` is that placement when a consumer needs it: the read, the local, and its
`LocalType`. `EmitScope.Yield` spills a computation into the next pool local.

**Bitcasts only at a word boundary.** `BitConverter.DoubleToInt64Bits` /
`Int64BitsToDouble` (wrapped once, in `RingCodec.ToIeee` / `FromIeee`) appear only where
a value must move as a word:

| Boundary | Why |
|----------|-----|
| `_slots[_fp + n]` read/write | a frame slot is a `long` word |
| the `_slots` observation copy | inside the hook guard, so `VmDebugger.GetLocals` can read a run that had a hook attached |
| `long[1]` capture cells | the cell is one `long` |
| `VmCall.Invoke` result and CLR call results | a call answers in the caller's word |
| a `RingRegion` slot | regions are word-addressed, and every region consumer reads a word |
| the root copy-out | the program's result is a word |

Those are per statement, per call, or per region — not per access. A `float` stays
`float` throughout an expression and widens only when it becomes a word, because the
word form of both IEEE widths is a `double`'s bits.

The tree rule is checked structurally: `VmTree.Scan.IeeeArithmeticWithBitcasts` lists
every IEEE arithmetic node whose operand or result converts at the operation, and
`VmTree.Scan.RegisterBitcasts` lists every bitcast that reads or is stored into a
variable lease. The scan uses the `LocalType` registry (an IEEE local with
`DoubleToInt64Bits`, or a non-IEEE local with `Int64BitsToDouble`), so a lease
named `_rN` or `_dN` is still visible. The frame snapshot and a call argument
are excluded.
`IeeeRingSlotTests` and `TypedVariableStorageTests` fail if either list is non-empty.

`CompileNode` returns a `CompiledNode`: the code plus the `SlotLocation` that holds
its result, so nothing has to infer the result from slot depth. Every arm leaves its
result at `RingDepth - 1` — a value node leaves its value, and a node with no value
(a loop, a branch, a jump, a throw) leaves `0L` — so a caller reads a node's result
without first asking what kind of node produced it, and there is no second value
compiler. The reported location takes its representation from the arm's own expression
type, so producer and consumer cannot disagree about which local was written. A
`Comment` emits nothing and keeps the previous statement's result, so it
is the one node with no ring location; a consumer that asks for one fails closed. A
block restarts each statement at the block's entry depth, so the high-water is the
deepest single statement, and a result is kept only when it is the block's value.

## Array Element Access

An array is a heap object: the variable's slot holds its handle, and a VM array is a
`long[]` when the element is a slot word and an `object[]` when it is a handle.

**A promoted array is cached in a typed CLR local.** A local declared in a block whose
only value is one `new T[n]`, and whose every other reference is the receiver of an
`IndexAccess`, binds the array object to a local at its definition:

```
_arr3 = NewArrayBounds(elem, len);     // the real array, created once
slot  = heap.Allocate(_arr3);          // the same object, as today
```

An element access is then `_arr3[i]` — one bounds check, no `heap.UnsafeGet`, no
`castclass`. The slot keeps the handle, so identity is preserved by construction and
nothing else about the variable changes; the local is a **cache**, not a replacement.
An out-of-range index still throws `IndexOutOfRangeException` from the CLR check.

**The local is keyed by `(slot, element type)`** (`Context.PromoteArray`), not by the
`Variable`, because the rest of the emitter resolves bindings by slot and a slot is
released at the end of a block and handed to the next sibling scope's variable —
`_arrN` for a lease index, `_farrN` for a frame slot. Two element types never alias
one local: a `long[]` and an `object[]` cannot be assigned to the same
`ParameterExpression`.

**The promotion predicate** is all-or-nothing per variable; anything else keeps the heap
path:

| Must hold | Why |
|-----------|-----|
| assigned exactly once, at a statement of the declaring block, before every other statement that references it | the local must be assigned before every read |
| the assigned value is a `NewArray` whose element type resolves (`ArrayElementClrType`) | the local's type is the array shape |
| every other reference is the `Value` of an `IndexAccess` | `.Length`, an argument, a return, or a block value is a use of the array object, not an element |
| no reference inside a nested `Lambda` or function body | that body is a separate delegate and cannot see the local |
| not captured (`Context.NeedsCell` is false) | a cell-backed local holds a cell handle, not the value |

**The block clears the local at scope exit** (`EmitBlock` appends
`Assign(cacheLocal, Constant(null))` after `Context.PopScope`). Because slots are reused
across sibling scopes, a surviving local would let a later block read the *previous*
block's array through a fresh binding; the clear makes that fail closed
(`NullReferenceException`) instead of silently returning the wrong buffer.

**The exit also releases the handle** (`EmitBlock` emits `heap.Recycle(slot)`
beside the clear, reading the handle from the variable's slot before clearing the
local). The promotion predicate is the VM's only proof that a heap object can never be
observed again — not returned, not passed, not aliased, not captured, and every other
reference an `IndexAccess` receiver — so it is the only place a handle is recycled.
A definition that is itself the block's **value** is the one case the predicate cannot
see, because the value it produces *is* the handle; that handle stays live
(`HeapLifetimeTests.BlockValueArray_IsNotRecycled`). Everything the VM cannot prove dead
is retained for the heap's lifetime.

That is what bounds a loop that allocates: ten thousand iterations of a block-local
`new long[4]` keep the heap's high-water mark at the working set instead of growing it
once per iteration (`HeapLifetimeTests.ArrayInLoopBody_HeapStaysBounded`). An array that
escapes as a return value, an argument, or an alias is not promoted and is never recycled
(`HeapLifetimeTests.EscapingArray_IsNotRecycled`).
`ArrayLocalCacheTests.SiblingBlock_SlotReuse_ReadsNullNotStaleArray` is the test for the
slot reuse the clear makes safe.

## Compilation Modes

| Mode | DebugHook | Resume dispatch | Use Case |
|------|-----------|----------------|----------|
| `Normal` | Active | Only when a `SuspendNode` recorded a step | Development, debugging, stepping |
| `NoDebug` | Omitted | Omitted | Production, benchmarks |

In `NoDebug` mode, the emitted expression tree omits all debug checks,
producing a smaller and faster delegate.

## Debug Hooks

| Hook | Call Frequency | Arguments | Overhead When Null |
|------|---------------|-----------|-------------------|
| `DebugHook` | Every AST statement (Normal) | `Node`, `ReadOnlySpan<long>`, `VmManagedObjectHeap` | Single null check |

`DebugHook` provides symbolic AST context without exposing the full state, and the
locals span is built at compile time. `VmState.DebugInterrupt` is a legacy member
the emitter does not invoke.

## Value Marshalling

`RingCodec` handles conversion between VM internal representation and
CLR types (`FromClr`/`ToClr` at compile time, `ToRing`/`Cast`/`TypeAs`/
`UnboxNullable` at runtime). `RingCodec.ToIeee`/`FromIeee` are the one writer and
reader of the double/long bitcast.

Where the slot lives is `SlotLocation` (`Ring`, `Frame`, `Local`, `Cell`). There is
no `View`. `Context.Resolve` maps a binding to one, so an emit site asks for the
location instead of re-deriving `_slots[_fp + n]`. Each location states the
`Representation` it holds — a typed lease or a typed pool local holds the value, a
frame slot and a cell hold the word — and `ReadAs` / `Write` convert only when the
requested representation differs. A typed read of a stored word is
`LocalType.FromWord`; a typed write is `Encode`. A value crosses the word boundary
once at the crossing. Two classifiers
answer two questions: `RingCodec.UnderlyingKindFor`
(does the *underlying* type fit one slot as bits; peels `Nullable`) and
`ValueRepresentationClassifier.KindFor` (what does a *node* of this type put in
its slot; a nullable is a heap handle).

The table is the word a frame slot, a cell, or a call result holds. A pool local
for `int`, `float`, or `double` holds the CLR value; `Encode` writes this word.

The representation table:

| CLR Type | VM Representation | Direction |
|----------|-------------------|-----------|
| `int`, `long`, `float`, `double` | Stack scalar (`long` bit pattern) | Both |
| `bool` | 0 = false, non-0 = true | Both |
| `short`, `byte` | Stack scalar (converted to/from `long`) | Both |
| Reference types | Heap handle (`int` index) | Both |
| Value types | Stack scalar or heap (depends on size) | Both |
