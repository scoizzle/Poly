# DAS W4.1 — Runtime: no semantic fallback scans when Domain bound

**Wave:** W4 · **Queue:** [`das-README.md`](./das-README.md)  
**Future state:** [`../domain-analysis-future-state.md`](../domain-analysis-future-state.md) P8, §5.1, W4  
**Difficulty:** Medium  
**Status:** `[x]`  
**Prereq:** W1 gate, W2 gate  

## Objective

When `Domain` is non-null, runtime uses catalog/helpers only. Remove `DM-META-REMOVE-FALLBACK` scans in `DomainEntityInstance` / `DomainInstanceStore` (and related) for action/stage/relationship semantic resolution.

## Tasks

- [x] W4.1.1 Grep markers under DomainEntityInstance / DomainInstanceStore; delete scan branches when analysis present/domain bound.
- [x] W4.1.2 Define standalone (`Domain == null`) contract: unsupported for semantic dispatch **or** reduced documented surface—no silent SA dual implementation.
- [x] W4.1.3 Fail closed if catalog/required bags missing.
- [x] W4.1.4 Tests: domain-bound paths; standalone behavior explicit.

## Acceptance criteria

- [x] Zero fallback markers in those runtime files (or ADR exception).
- [x] Build + tests green; sibling-path N/A (single path).

## Progress notes

### 2026-07-31 — implement + verify (pass, severity nit)

**Implement success:** true · **Verify:** pass (nit)

- Grep: zero `DM-META-REMOVE-FALLBACK` in `DomainEntityInstance.cs` and `DomainInstanceStore.cs`.
- Domain-bound `InvokeActionInternal`: requires `GetActionResolution` (throws if null) then `TryResolveAction(Domain, …)` only — no structural scan.
- Stage guards require `EntityStructureMetadata` (fail closed). `TransitionStage` uses `TryGetStage` when analysis present; `Entity.Stages` only when `Domain == null`.
- `CreateChildInstance` / create-in / outbound relationships: `TryGetEntity` / `TryGetRelationship` fail-closed (catalog/RLM).
- `NotifyTransition`: early-returns when `Domain == null`; else RCM/ESM/SDPM fail-closed (no `Domain.Relationships` scan).
- Standalone (`Domain == null`) **reduced contract** on type remarks + `ResolveStandaloneAction`: structural SA fallthrough only; no subscriptions; no relationship/`create in` semantic resolve.
- Tests: `InvokeAction_DomainBound_*` / `Standalone_*`; `DomainInstanceStoreFailClosedTests` (store suite green). Targeted: `DomainEntityInstanceTests` 69/69; `DomainInstanceStoreFailClosedTests` 5/5.
- Residual `DM-META-REMOVE-FALLBACK` markers remain in Evolution / Lowering / Oracle / DslCompiler → **W4.2 / W4.3** (out of scope).
