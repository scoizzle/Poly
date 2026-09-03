# VM Trampoline Dispatch

## Summary
Replace the 570-line `Vm.Execute` method (while loop + nested switch + SizeBit check) with a 256-entry `OpHandler[]` table indexed by the raw opcode byte. Each handler is an independent `[MethodImpl(AggressiveInlining)]` static method. The dispatch loop is ~15 lines. No SizeBit, no switch, no `continue`/`break` confusion.

## Current architecture

```
Vm.Execute (570 lines):
  while (codeOff < codeLength) {
    rawOp = code[codeOff]
    if (DebugMode && InterruptBit) → suspend
    if (SizeBit) → operand-bearing switch (20 cases)
    else         → nullary switch (30 cases)
    codeOff += 9 or 1
  }
```

Problems:
- `break` in the switch exits the switch, not the while loop — caused the `Return` fallthrough bug
- `codeOff += 9` after operand-bearing switch, `codeOff++` after nullary — shared fallthrough
- Every instruction pays the `SizeBit` branch even though the raw byte fully encodes the form
- Monolithic method: hard to test handlers in isolation

## Proposed architecture

### Handler delegate

```csharp
internal delegate void OpHandler(ref ExecutionState s);
```

### Dispatch table

```csharp
private static readonly OpHandler[] Dispatch = BuildTable();

private static OpHandler[] BuildTable() {
    var t = new OpHandler[256];
    // Nullary
    t[0x00] = Pop;
    t[0x01] = Dup;
    t[0x04] = Add;
    t[0x15] = Return;
    // Operand-bearing (SizeBit set)
    t[0x44] = Add;           // fused PushAdd — 0x04 | 0x40
    t[0x5B] = Push;
    t[0x5C] = Jump;
    t[0x5D] = JumpIfFalse;
    t[0x5E] = Call;
    t[0x5F] = CallExternal;
    t[0x67] = IncLocal;
    // ~78 non-null entries
    var err = Unsupported;
    for (int i = 0; i < 256; i++) if (t[i] is null) t[i] = err;
    return t;
}
```

### Dispatch loop

```csharp
public static InterpreterResult Execute(VmState state) {
    // null check, constant loading, ExecutionState setup ...
    try {
        while (s.PC < s.Program.CodeLength && !s.Vm.ShouldStop) {
            byte raw = s.Program.Code[s.PC];
            if (s.DebugMode && (raw & 0x80) != 0) {
                s.Vm.Status = InterpreterStatus.Suspended;
                break;
            }
            Dispatch[raw](ref s);
        }
        // ExtractResult ...
    }
    catch (Exception ex) { ... }
}
```

### Example handlers

```csharp
[MethodImpl(MethodImplOptions.AggressiveInlining)]
private static void Add(ref ExecutionState s) {
    s.Slot(s.SP - 2) += s.Slot(s.SP - 1);
    s.SP--;
    s.PC++;
}

[MethodImpl(MethodImplOptions.AggressiveInlining)]
private static void Push(ref ExecutionState s) {
    s.Slot(s.SP++) = Code64(s, s.PC + 1);
    s.PC += 9;
}

[MethodImpl(MethodImplOptions.AggressiveInlining)]
private static void Return(ref ExecutionState s) {
    if (s.FramePos < 0) { s.PC = s.Program.CodeLength; return; }
    long result = s.Slot(--s.SP);
    var f = s.CurFrame;
    s.SP = f.SavedSP;
    s.Slot(s.SP++) = result;
    s.PC = f.ReturnPC;
    s.FramePos = f.SavedFramePos;
}

[MethodImpl(MethodImplOptions.AggressiveInlining)]
private static void Jump(ref ExecutionState s) {
    s.PC = (int)Code64(s, s.PC + 1);
}

[MethodImpl(MethodImplOptions.AggressiveInlining)]
private static void JumpIfFalse(ref ExecutionState s) {
    if (s.Slot(--s.SP) == 0)
        s.PC = (int)Code64(s, s.PC + 1);
    else
        s.PC += 9;
}
```

## Files changed

| File | Change |
|---|---|
| `Vm.cs` | Replace `Execute` body with dispatch loop (~60 lines). Keep `CallFrame`/`Frame`, `FindRegion`, `ExtractResult`. |
| `Vm.Dispatch.cs` (new) | `BuildTable()` + all ~80 handler methods. |
| `ExecutionState` (new) | Ref struct with `Slot()`, `Local()`, `Arg()`, helpers. |

## Dispatch table readme

The table is indexed by the raw byte from the code stream. Nullary opcodes use the raw opcode value directly (0x00–0x27). Operand-bearing opcodes set bit 6 (0x40), so their index is `opcode | 0x40`. Both forms are separate entries.

```
                    Nullary entries      Operand-bearing entries
                    (opcode)             (opcode | 0x40)
Add:                0x04 → Add          0x44 → Add  (fused PushAdd)
Push:               —                   0x5B → Push (0x1B | 0x40)
Jump:               —                   0x5C → Jump
JumpIfFalse:        —                   0x5D → JumpIfFalse
Call:               —                   0x5E → Call
CallExternal:       —                   0x5F → CallExternal
IncLocal:           —                   0x67 → IncLocal (0x27 | 0x40)
Pop:                0x00 → Pop          —
Return:             0x15 → Return       —
```

Roughly 78 entries are non-null (39 opcodes × 2 forms, minus a few unused combos). The remaining 178 entries point to the `Unsupported` throw handler.

## Inlining expectations

Each handler is 3–10 IL bytes. `[MethodImpl(AggressiveInlining)]` on every handler combined with the tiny method size should cause the JIT to inline the handler bodies through the delegate call. The indirect call through the delegate becomes a direct jump to inlined code — identical to the switch table.

If profiling shows the delegate indirect call is still a bottleneck, the next step is switching from `OpHandler[]` to `IntPtr[]` + `delegate*`:

```csharp
private static readonly IntPtr[] Dispatch = BuildTable();
// Dispatch:
((delegate*<ref ExecutionState, void>)Dispatch[raw])(ref s);
```

This requires `unsafe` on the class. Eliminates the delegate object indirection and the extra load through the delegate's `_methodPtr`. The table becomes a pure `IntPtr[]` — 2 KB, zero managed references, zero GC tracking. Measurable gain is likely nil until profiling proves otherwise.

## Test plan
- All 1170 existing tests pass unchanged
- Each handler can be independently unit tested by calling `Dispatch[i](ref state)` and checking state changes
- Benchmarks within ±5% of current
- No `continue`/`break`/`codeOff += N` fallthrough logic in any handler
