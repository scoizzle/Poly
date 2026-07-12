# Micro-Task: Fail-loud domain evolution (missing targets)

**Suite:** [`vs-README.md`](vs-README.md) **#0.1**  
**Parent:** [`../vertical-slice-finish-plan.md`](../vertical-slice-finish-plan.md) Slice 0  
**Difficulty:** Small Model Friendly  
**Estimated Context:** ~6k tokens  
**Status:** [ ] Not Started  

## Objective

When a `DomainChange` targets a missing entity, stage, or action, `DomainEvolution.Apply` must **not** report success with zero effect.

## Required Reading (only these)

- `AGENTS.md` — fail-loud / customer honesty (platform trust blurb OK)
- `Poly/DomainModeling/Evolution/DomainMutationContext.cs`
- `Poly/DomainModeling/Evolution/DomainChange.cs` — a few `ApplyTo` that call `UpdateEntity` / `UpdateAction` / `UpdateStage`
- `Poly.Tests/DomainModeling/Evolution/EvolutionRollbackTests.cs` — any `SilentNoOp` tests

**Do not** read the full review plan or rewrite demos.

## Exact Steps

1. Confirm how `UpdateEntity` / `UpdateStage` / `UpdateAction` return `false` or no-op today when the target is missing.
2. Make missing targets fail the change: prefer **error diagnostic + rolled-back `EvolutionResult`** (or clear throw from `ApplyTo` that Apply turns into rollback). Match existing `EvolutionResult` patterns.
3. Fix `UpdateAction` so “entity found but action missing” is **failure**, not success.
4. Update tests that expected silent success to expect **failure / non-Succeeded**.
5. Add or tighten tests: add property to missing entity; attach effect to missing action; missing stage name.

## Verification

- [ ] `dotnet build` green
- [ ] Evolution tests green; no SilentNoOp-as-success left for missing targets
- [ ] MCP smoke still green if it depends on missing-entity failure (fingerprint path OK)
- [ ] No new evolution engine; no MCP-only fix

## Output

- Modified: `DomainMutationContext.cs` and/or `DomainChange.cs` / `DomainEvolution.cs`
- Modified tests under `Poly.Tests/DomainModeling/Evolution/`
- Summary: `../agent-summaries/vs-s0-fail-loud-evolution-summary.md`

## Out of Scope

- MCP fingerprint redesign
- All 66 change types polish
- Policy evaluation

## Status tracking

**Claimed by:**  
**Started:**  
**Notes / Blockers:**
