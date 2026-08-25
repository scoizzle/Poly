> **ARCHIVED (2026-07-10)** — Do not implement. Superseded by direct AST→VM-ABI (`DirectVmAbiEmitter`). See `docs/plans/archive/interpretation/README.md`.
>
> Original document follows for historical context only.

# Plan: VM Test Gap Closure

**Priority tiers** for the neurosymbolic loop:

## Tier 1 — Critical (blocking correctness)

| Gap | Why |
|-----|-----|
| `TryCatchFinally` | Exception handling is required for any robust execution |
| `ForEachLoop` | Iteration over collections is fundamental |
| `SwitchStatement` | Pattern matching dispatch |
| `NotEqual`, `Le`, `Ge` | Missing comparison ops (asymmetry in coverage) |
| `ContinueStatement` | Loop control is half-tested |
| `UsingStatement` | Resource management |
| Lambda closure capture mutation | `StoreUpvalue`/`LoadUpvalue` never tested |

## Tier 2 — High (should work)

| Gap | Why |
|-----|-----|
| Unsigned arithmetic (`UDiv`, `UMod`, etc.) | Lowering resolves these via type analysis |
| Double comparisons (`DEq`, `DNe`, `DLt`, etc.) | Same — lower path is exercised, VM path is not |
| `New` / constructor | Object construction via lowering |
| `ThrowStatement` | Direct throw (vs div-by-zero implicit) |
| Assignment to member/indexer | Property/indexer setter via lowering |
| `CallClosure` | Generic delegate dispatch |
| `LabelDeclaration` + `GotoStatement` | Control flow |

## Tier 3 — Medium (edge cases)

| Gap | Why |
|-----|-----|
| `Iret` resume | Interrupt resume path |
| Breakpoint PC check | Debugger infrastructure |
| Heap.Get bounds | Robustness |
| `PushLong` direct | Long constant testing |
| Narrow modes 1-4 | Type narrowing completeness |
| All optimizer fold patterns | Optimizer correctness |
| `Await` | Async simulation |

## Implementation Order

1. **Tier 1 tests** — add to `VmParityTests.cs` (AST lowering path)
2. **Tier 2 tests** — add to both `VmParityTests.cs` and `VmSkeletonTests.cs`
3. **Tier 3 tests** — add where infrastructure is already validated

Total: ~40 new test methods expected.
