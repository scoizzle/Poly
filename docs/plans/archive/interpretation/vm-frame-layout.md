# VM Frame Layout & Offset Reference

## Stack Layout Per Call Frame

Each function call reserves a contiguous region of the VM's slot-array (each
slot = one `long` = 8 bytes).  The layout is identical for `Call_Imm`,
`CallClosure_Imm`, and the NativeFn path:

```
        low index
     ┌─────────────────────────────┐
     │  caller's eval stack         │
     ├─────────────────────────────┤
     │  arg[0] (first arg)          │ ← ESB (Effective Stack Base = frameBase)
     │  arg[1]                      │
     │  ...                          │
     │  arg[ArgSlots-1]             │
     ├─────────────────────────────┤
     │  metadata: one packed long   │ ← ESB + ArgSlots
     │  (returnPC<<32)|savedFB      │
     ├─────────────────────────────┤
     │  local[0]                    │ ← ESB + ArgSlots + 1
     │  ...                          │
     │  local[LocalCount-1]        │ ← ESB + ArgSlots + LocalCount
     ├─────────────────────────────┤
     │  eval stack (grows up)       │ ← SP = ESB + ArgSlots + LC + 1
     │  [temp / result]             │
     │  ...                          │
        high index
```

### Slot Counts Per Function Are Precomputed

`FunctionEntry` stores field counts directly (no division at runtime):

| Field | Source |
|---|---|
| `ArgSlots` | `paramCount`, or `paramCount + 1` for lambdas (closure slot) |
| `RetSlots` | always 1 |
| `LocalCount` | number of local variables discovered by analysis |

## Offset Formulas

All offsets are computed as `Slot(baseSlot, frameBase + offset)`.  The
operands for LoadLocal/StoreLocal/IncLocal are precomputed at lowering time
(see "Precomputed Local Offsets" below), but the runtime formula is the same.

### Argument Access

```csharp
LoadArg(i):   Slot(ref baseSlot, frameBase + i)
StoreArg(i):  Slot(ref baseSlot, frameBase + i)
```

No subtraction needed — ESB points directly at `arg[0]`.  For lambdas,
`arg[0]` is the closure handle (or `-1` for direct calls with a dummy
closure).  User parameters start at `arg[1]`.

### Local Variable Access

```csharp
LoadLocal(i):   Slot(ref baseSlot, frameBase + ArgSlots + 1 + i)
StoreLocal(i):  Slot(ref baseSlot, frameBase + ArgSlots + 1 + i)
IncLocal(i):    Slot(ref baseSlot, frameBase + ArgSlots + 1 + i) += inc
```

The `+ 1` accounts for the metadata slot (`Slot(ESB + ArgSlots)`).
Local[0] is at `Slot(ESB + ArgSlots + 1)`.  See the layout diagram above.

### Upvalue Access

The closure handle is always at `arg[0]`:

```csharp
LoadUpvalue(i):   handle = Slot(ref baseSlot, frameBase)
                  captures[upi] from handle's closure object
StoreUpvalue(i):  handle = Slot(ref baseSlot, frameBase)
                  handle's closure.Captures[upi] = value
```

## Call and Return Handlers

### Call_Imm

Before handler: `SP = X + ArgSlots` (N args pushed by bytecode).

```csharp
newFp    = SP - ArgSlots                    // ESB = first arg
SP       = SP + 1                           // reserve metadata slot
Slot(SP) = ((returnPC << 32) | (uint)(int)frameBase)  // write packed metadata
frameBase = newFp
SP       = frameBase + ArgSlots + LocalCount + 2
PC       = entry.PC
```

### CallClosure_Imm

Identical to `Call_Imm` except `ArgSlots` comes from the operand (bytecode),
and the closure handle is read from `Slot(SP - ArgSlots)` (which is `arg[0]`).

### Return_Imm

Before handler: `SP` is at the callee's eval stack top (result is at `SP-1`).

```csharp
result   = Slot(--SP)                       // pop result
packed   = Slot(frameBase + ArgSlots)       // read metadata
SP       = frameBase                        // rewind to arg[0]
Slot(SP) = result; SP++                     // write result, advance
frameBase = (int)packed                     // restore caller's frameBase
PC       = (int)(packed >> 32)              // restore caller's PC
```

After Return, `SP = frameBase + 1` (caller has one result on eval stack).

### Top-level Return (nullary `Return`)

When `frameBase < 0` the program is at the top level — set `PC = codeLength`
to exit.  No metadata read, no stack manipulation.

## Metadata Encoding

One `long` (8 bytes) per call frame, live on the data stack:

```
bits 63..32:  returnPC  (int) — address after the Call* instruction
bits 31..0:   savedFB   (int) — caller's frameBase, or -1 for top-level
```

Encoding on Call:  `Slot(SP++) = ((long)(returnPC) << 32) | (uint)(int)savedFB`
Decoding on Return: `frameBase = (int)packed;  PC = (int)(packed >> 32)`

**Important:** The `savedFB` must be cast through `(uint)(int)` to zero-extend
when `savedFB` is negative.  A top-level `savedFB = -1` sign-extends to
`0xFFFFFFFFFFFFFFFF` as a `long`, which would OR away the returnPC bits if
not masked to `0x00000000FFFFFFFF`.

## Precomputed Local Offsets (Lowering Time)

Instead of computing `ArgSlots + 1 + i` at runtime for every LoadLocal
(the runtime formula), the lowering pass bakes the full offset into the
operand:

```csharp
int localBase = entry.ArgSlots + 1;
ctx.Code.Emit(OpCode.LoadLocal_Imm,  localBase + i);
ctx.Code.Emit(OpCode.StoreLocal_Imm, localBase + i);
ctx.Code.Emit(OpCode.IncLocal_Imm,   ((long)(localBase + i) << 32) | inc);
```

The handler for all three is unified:

```csharp
case OpCode.LoadLocal_Imm: {
    int off = (int)Code64(ref codeRef, pc + 1);  // already includes base
    Slot(ref baseSlot, sp++) = Slot(ref baseSlot, frameBase + off);
}
```

LoadArg/StoreArg offsets do not need precomputation because arg indices are
already 0-relative to ESB (`Slot(FP + i)`).

## Example Walkthrough: `() => 42`

Bytecode layout:
```
PC=0:  Push -1         (9 bytes, dummy closure = arg[0])
PC=9:  Call_Imm 1<<32  (9 bytes, funcIdx=0, argSlots=1)
PC=18: Return           (1 byte, top-level)
PC=19: Push 42          (9 bytes, lambda body)
PC=28: Return_Imm 1<<32 (9 bytes, argSlots=1)
```

Execution trace:

| Step | PC | Op | SP | FB | Notes |
|---|---|---|---|---|---|---|
| 1 | 0 | Push -1 | 1 | -1 | Closure at Slot(0) |
| 2 | 9 | Call(0,1) | 2 | 0 | Slot(0)=ESB; metadata at Slot(1); SP=2 |
| 3 | 19 | Push 42 | 3 | 0 | Slot(2)=42 |
| 4 | 28 | Ret(1) | 1 | -1 | Pop 42→Slot(0); SP=1; PC=18 |
| 5 | 18 | Return | 1 | -1 | Top-level: PC=exit |
| 6 | — | Extract | 0 | -1 | --SP=0 → Slot(0)=42 → result 42 |

## Why These Specific Offsets

| Constant | Reason |
|---|---|
| ArgSlots | Number of argument slots = `paramCount` + (1 for closures) |
| Metadata at `ESB + ArgSlots` | Written by `Slot(sp++)` — metadata is one slot above the last arg |
| local[0] at `ESB + ArgSlots + 1` | Immediately after the metadata slot |
| SP = `ESB + ArgSlots + LC + 1` | One past the last local, ready for eval stack |
