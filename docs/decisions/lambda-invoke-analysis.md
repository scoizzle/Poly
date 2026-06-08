# Lambda-Invoke Root-Cause Analysis

**Date:** 2026-06-08  
**Status:** Resolved  
**Session context:** VM lowering produced zero-byte output for all nodes; bytecode was always `Jump + Return`.

## Root Cause

Two dangling `if` statements with empty bodies were present in `Lowering.cs`, causing the C# compiler to interpret the **next statement as the `if` body**. Both were remnants of an earlier debugging session.

### Bug 1: `if (node is Variable vnode)` — Line 472

```csharp
int emitPc = code.Count;
sourceMap[emitPc] = node.Id;

if (node is Variable vnode)    // <-- empty body

switch (node) {                // <-- becomes the if-body!
```

The `switch` became the body of `if (node is Variable)`. When the node was NOT a Variable (e.g. `Add`, `Invoke`, `Block`), the condition was false, so the entire switch was skipped. **Every `Emit` call produced zero bytes.**

**Symptom:** All VM tests failed with `HasValue=false`; bytecode was always 6 bytes (`Jump target=6 + Return`). The root expression and lambda bodies emitted nothing.

### Bug 2: `if (variable.Name == "outer")` — Line 744

```csharp
case Variable variable:
    if (variable.Name is null) return;
    if (variable.Name == "outer")        // <-- empty body
    if (paramIndexMap is not null ...) { // <-- becomes the if-body!
```

The entire LoadArg/LoadLocal/LoadUpvalue resolution chain became conditional on `variable.Name == "outer"`. For any variable named differently (e.g. `"i"`, `"x"`), no load instruction was emitted. **Variables resolved to nothing.**

**Symptom:** Lambda body `Variable("i")` or `Variable("x")` emitted no bytecode; function body consisted of only the Assignment push/dup/store, then the comparison operand (the constant) was pushed without the variable's value. Results were garbage or IndexOutOfRangeException from stack underflow relative to frame boundary.

## Impact

| Scope | Tests affected | Root cause |
|-------|---------------|------------|
| All VM lowering | 45 failing tests (all restored `VmParityTests.cs` + 2 existing `VmSkeletonTests`) | Bug 1: switch swallowed |
| Lambda variable access | ~8 failing tests (variables inside lambda bodies) | Bug 2: Load-chain swallowed |

Both were introduced during the tree-walker removal / lowering refactoring session — likely stripping what was thought to be debug code but leaving the `if` header.

## Fix

Remove the two standalone `if` statements (lines 472–473 and 744 in the current file).

## Verification

- **1195 tests passing** (Debug), **1194** (Release — Debug-only tracing test excluded)
- Zero failures in Debug and Release configurations
- Affected scenarios verified:
  - Simple arithmetic (int, double, string)
  - Control flow (if/else, while, do-while, for, switch)
  - Short-circuit (And, Or)
  - Type tests (TypeIs, TypeCast)
  - Member/index access
  - Lambda invocation with local variables and loops
  - Named break/continue
  - Try/catch/finally
  - ForEach and Using
  - CLR method invocation via lowering

## Preventive Note

These bugs were invisible from compilation (valid C#) and surfaced only at runtime. A code review of `Emit` specifically looking for empty `if` bodies or switch-statement-as-if-body patterns would have caught both immediately. Consider adding an analyzer rule for `if` statements with empty (whitespace-only) bodies.
