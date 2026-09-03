# VM Decoupled Frame Model

## Summary
Replace the embedded 4-slot `CallFrame` header on the eval stack with a 2-slot `Frame` struct. Eliminates `FrameBase`, `CachedArgSlots`, `FrameHeaderSlots`, `Array.Copy` in Return, and the `SavedBase → 0` bug class.

## Current architecture
- Frame header is 4 `long` slots embedded in the eval stack: `[retPC][savedBase][argSlots][retSlots]`
- `FrameBase` and `CachedArgSlots` are mutable fields on `VmState`
- `LoadLocal i` computes `FrameBase + FrameHeaderSlots + i`
- `LoadArg i` computes `FrameBase - CachedArgSlots + i`
- `Return` uses `Array.Copy` to move result from top-of-stack to `preArg = FrameBase - argSlots`
- `prevBase` conversion bug: `state.FrameBase < 0 ? 0 : state.FrameBase` at Call, fixed but fragile

## Proposed architecture

### Frame struct (2 slots = 16 bytes)

```csharp
[StructLayout(LayoutKind.Sequential)]
private readonly struct Frame {
    public readonly int ReturnPC;
    public readonly int SavedFramePos;   // -1 = top-level
    public readonly int SavedSP;
    public readonly short ArgCount;
    public readonly short LocalCount;
}
```

### ExecutionState

```csharp
internal ref struct ExecutionState {
    private ref long _base;
    public int SP;
    public int PC;
    public int FramePos;        // index of current Frame on eval stack (-1 = top-level)

    public const int FrameSlots = 2;

    public ref long Slot(int i) => ref Unsafe.Add(ref _base, i);
    public ref Frame CurFrame  => ref Unsafe.As<long, Frame>(ref Slot(FramePos));
    public ref long Local(int i) => ref Slot(FramePos + FrameSlots + i);
    public ref long Arg(int i)   => ref Slot(FramePos - CurFrame.ArgCount + i);
}
```

### Call handler

```csharp
int argSlots = (int)Slot(--SP);
int fp = SP + 1;
Unsafe.As<long, Frame>(ref Slot(fp)) = new(
    ReturnPC: PC + 9,
    SavedFramePos: FramePos,
    SavedSP: fp - argSlots,
    ArgCount: (short)argSlots,
    LocalCount: (short)entry.LocalCount);
SP = fp + FrameSlots + entry.LocalCount - 1;
FramePos = fp;
PC = entry.PC;
```

### Return handler

```csharp
long result = Slot(--SP);
var f = CurFrame;
SP = f.SavedSP;
Slot(SP++) = result;
PC = f.ReturnPC;
FramePos = f.SavedFramePos;   // -1 → top-level
```

## Handler signature (trampoline-ready)

```csharp
internal delegate void OpHandler(ref long stack, ref int sp, ref int pc, VmState vm);
```

Hot-path fields (`stack` base, `sp`, `pc`) are direct ref parameters — no struct indirection.
`framePos` is another local in `ExecuteNew` (or a fifth ref parameter in the trampoline).
Everything else lives on `VmState`.

## Changes by file

| File | Change |
|---|---|
| `Vm.Execute.New.cs` | New `ExecuteNew()` using locals only: `baseSlot`, `sp`, `pc`, `framePos`. No `ExecutionState` struct. |
| `Frame.cs` | New 2-slot `Frame` struct. |
| `VmState.cs` | No changes. |
| `JitCompiler.cs` | No changes — still receives `VmState`. |
| `CallSiteCompiler.cs` | No changes. |
| `LoopBodyEntry.cs` | No changes. |

## What's eliminated

| Concept | Removed | Replaced by |
|---|---|---|
| `FrameHeaderSlots = 4` | Constant | `FrameSlots = 2` |
| `FrameBase` (VmState) | Property | `FramePos` (int on ExecutionState) |
| `CachedArgSlots` (VmState) | Property | `Frame.ArgCount` |
| `CallFrame` struct | Struct | `Frame` struct (2 slots) |
| `Array.Copy` in Return | Call | `SP = SavedSP; Slot(SP++) = result` |
| `preArg` arithmetic | Calc | `SP = SavedSP` |
| `prevBase → 0` bug | Code path | `SavedFramePos = -1` natively correct |
| `newBase + 4 + LocalCount` | Calc | `fp + 2 + LocalCount` |

## Test plan
- All 1170 existing tests must pass unchanged
- Frame size verification: `Unsafe.SizeOf<Frame>() == 16` at startup
- `Call`/`Return` round-trip preserves `SavedFramePos = -1` at top level
- `LoadLocal`/`StoreLocal`/`LoadArg`/`StoreArg` access correct slots
- `IncLocal` increments correct slot
- `CallExternal` syncs `FramePos` correctly
- JIT function path (`NativeFn`) works with new frame model
