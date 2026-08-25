# VM JIT Tier — Implementation Plan

## Summary
Add a tiered execution layer to the Poly VM: hot functions (Lambda/MethodDefinitionNode) are compiled to native `CallSiteDelegate` delegates via `LinqExpressionGenerator` after a threshold number of invocations. The JIT reuses the same `void(VmState)` ABI as `CallExternal`, so dispatch is unified — only the target changes.

## Files Changed

### 1. `Bytecode.cs` — data model
- **`FunctionEntry`**: add `int HotCount`, `CallSiteDelegate? NativeFn`, `Node? SourceNode`
- **`Bytecode`**: add `AnalysisResult? AnalysisResult` property + constructor param
- Fix `BuildProgram` to pass `ctx.SourceMap` through (currently silently discarded)

### 2. `Lowering.cs` — preserve what the JIT needs
- After pre-scan phase, store `Lambda`/`MethodDefinitionNode` in each `FunctionEntry.SourceNode`
- Pass `analysis` into `BuildProgram` → `Bytecode` constructor
- Fix `BuildProgram` call to pass `ctx.SourceMap` instead of `[]`

### 3. `Vm.cs` — JIT dispatch in Call/CallClosure
- **`Call` handler**: before frame push, check `entry.NativeFn`
  - If present + `!DebugMode` → dispatch like `CallExternal` (SetSP, call delegate, restore spOff/codeOff)
  - Detect `state.JITFallbackRequested` flag → restore stack, fall through to bytecode
  - If absent + source node + hot count > threshold → call `JitCompiler.Compile()`
- **`CallClosure` handler**: same pattern, but closure handle at stack index 0 is passed through
- Add `VmState.JITFallbackRequested` flag (bool)

### 4. `JitCompiler.cs` — new file (~80 lines)
- `Compile(FunctionEntry, AnalysisResult) → CallSiteDelegate`
- For `Lambda`:
  1. Create `LinqExpressionGenerator(analysis)`
  2. `generator.Compile(lambda.Body)` → `Expression`
  3. Build wrapper `Expression` that:
     - Checks `state.DebugMode` → sets `JITFallbackRequested + return` if true
     - Reads captures from closure handle (arg 0 via `state.Heap`)
     - Reads user params from stack via `RawSlots`
     - Calls compiled expression with typed args
     - Writes result to `RawSlots[baseOff]` (overwriting first arg)
     - Updates `state.Stack.SP`
  4. `Expression.Lambda<CallSiteDelegate>(...).Compile()`
- For `MethodDefinitionNode`: same pattern, no closure handle

### 5. `VmState.cs`
- Add `bool JITFallbackRequested` property
- `Reset()` clears it to false

## Constants
```csharp
// In Vm.cs
internal const int JitThreshold = 10;
```

## Edge Cases

| Case | Handling |
|---|---|
| DebugMode set after JIT | Entry guard in delegate checks `state.DebugMode`, sets `JITFallbackRequested`, VM retries via bytecode |
| Lambda captures | Closure handle at stack index 0 — delegate reads via `state.Heap.Get(handle)` |
| Thread safety | `Interlocked.Increment` on HotCount; `LazyInitializer.EnsureInitialized` on NativeFn |
| Compilation latency | Synchronous on threshold-crossing call (~1-5ms for small functions) |
| Exception in JITted code | Propagates through delegate call, caught by `Vm.Execute` catch block |
| SourceMap for future debugger | Fix transfer from `ctx.SourceMap` to `Bytecode` (currently discarded) |
