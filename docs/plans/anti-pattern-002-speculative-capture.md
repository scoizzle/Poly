# Anti-Pattern 002: Speculative Capture in Bytecode

**Problem:** `Bytecode` carries fields that are populated during lowering but never queried by any consumer. Each field normalizes the "capture everything" pattern, making it harder to distinguish genuinely needed payload from speculative capture.

## Fields

| Field | Consumer | Status |
|---|---|---|
| `MicroOps` | VM, debugger, Dump | Active |
| `Functions` | VM (call/return), debugger | Active |
| `Constants` | VM (heap pre-load) | Active |
| `CallSites` | VM (CallExternalOp) | Active |
| `ExceptionRegions` | VM (HandleThrow) | Active |
| `NodeRanges` | Debugger (step-over) | Active |
| `ResultType` | VM (result extraction) | Active |
| `AnalysisResult` | None | Speculative |
| `CallSiteTargets` | None | Speculative |
| `LoopBodies` | None | Speculative |
| `FunctionEntry.SourceNode` | None | Speculative |
| `FunctionEntry.RetSlots` | None (always 1) | Speculative |

## Plan

1. **Add a `BytecodeSpec` record** that holds the speculative fields. Make it nullable on `Bytecode`.
   ```csharp
   internal sealed record BytecodeSpec(
       AnalysisResult? AnalysisResult,
       IReadOnlyList<string>? CallSiteTargets,
       IReadOnlyList<LoopBodyEntry>? LoopBodies
   );
   ```

2. **Move `SourceNode` from `FunctionEntry` to a separate dictionary** or remove it entirely. If no debugger consumer needs it within 6 months, delete it.

3. **Remove `RetSlots` from `FunctionEntry`.** It's always 1 and never read. If multi-return functions are ever needed, it's added back as a constructor parameter.

4. **Update all consumers** that currently receive the full `Bytecode` to handle nullable `BytecodeSpec`.

**Lines saved:** ~30 lines of construction + storage, plus `LoopBodyEntry.cs` (6 lines) if `LoopBodies` is removed.

**Risk:** Low — no consumer reads these fields. The `BytecodeSpec` can be null and everything works.

**Timeline:** 30 minutes.
