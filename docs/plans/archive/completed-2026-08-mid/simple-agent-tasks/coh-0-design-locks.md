# COH-0 — Design locks and file ownership

**Difficulty:** S  
**Status:** `[x]` — PASSED 2026-08-06

## Design locks (confirmed)

1. **Option C incremental path:** `Runtime/` folder first; **no** `Parsing→Dsl`
   move this suite (README hard rule + coh-r1 objective).
2. **Namespace decision (R1):** keep `Poly.DomainModeling` with folder-only move
   (coh-r1 preferred option) — minimizes usings churn; folder signals grouping,
   namespace stays stable for consumers (MCP tools, tests, exporters).
3. **Behavior-preserving:** every chain must land with full suite green and no new
   product claims. No multi-project split (single Poly project).
4. **Execution order this session:** COH-0 → **R1 → D1 → E1 → V1 → gate** — R
   before D (both touch `DomainEntityInstance`); E after R (coordinates move);
   V independent (Evolution/ only).
5. **Dispatch naming:** methods named by type, not Visit* (AGENTS naming).

## File ownership (locked)

| Chain | Primary production files |
|-------|--------------------------|
| R | `DomainEntityInstance.cs`, `DomainInstanceStore.cs`, `InvocationResult.cs` → `Runtime/` |
| D | Expression rewrite methods in DomainEntityInstance (or extracted helper) + `DomainExpressionDispatch.cs` |
| E | `EffectAnalyzer.cs` (+ EffectDispatch) — coordinate if R moves EntityInstance |
| V | `DomainChange.cs`, `DomainMutationContext.cs` |

Chains must not edit each other's primary files (hard rule).

## Verification

- [x] Ownership table in notes
- [x] R-before-D rule explicit

## Status

**Status:** DONE — locks recorded; chain heads start at R1.  
