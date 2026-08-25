# Micro-Task: Surface missing-target evalErrors in diagnostics / FailureSummary

**Suite:** [`vs-README.md`](vs-README.md) **#0.1a**  
**Parent:** Slice 0 (residual of **#0.1**)  
**Depends on:** **#0.1** Done  
**Difficulty:** Small Model Friendly  
**Estimated Context:** ~4k tokens  
**Status:** [x] **Done** (2026-07-12) — `DomainEvolution.Apply` injects each evalError as `DiagnosticSeverity.Error` with code `EVOLUTION_TARGET` before `Diagnostics` is materialized. Optional residual: assert `FailureSummary` on the missing-entity unit test (other FailureSummary asserts already exist for analysis failures).  

## Objective

When `Apply` rolls back because of `DomainMutationContext.Errors` (missing targets), agents must see those messages on the result — not only `Succeeded: false` with an empty `FailureSummary`.

## Background

Today `evalErrors` set `hasErrors` and trigger rollback, but they are **not** written into `Analysis.Diagnostics`. `EvolutionResult.FailureSummary` only reads Error diagnostics, so pure missing-target failures can show a blank/useless summary.

## Required Reading

- `Poly/DomainModeling/Evolution/DomainEvolution.cs` — `Apply` (evalErrors handling)
- `Poly/DomainModeling/Evolution/EvolutionResult.cs` — `FailureSummary`
- `Poly/Syntax/Analysis/Diagnostic.cs` (how to construct Error diagnostics)
- `Poly.Tests/DomainModeling/Evolution/EvolutionRollbackTests.cs` — `Apply_AddPropertyToMissingEntity_FailsLoudAndRollsBack`

## Exact Steps

1. After analysis (or when building the rolled-back result), for each string in `evalErrors`, add a `Diagnostic` with `DiagnosticSeverity.Error` and a stable code (e.g. `EVOLUTION_MISSING_TARGET`) on the domain node (`proposed` or original root — prefer attaching so `Analysis.Diagnostics` lists them).
2. Prefer mutating the diagnostics dictionary the same way `EVOLUTION_STEP` infos are added (existing pattern in `Apply`).
3. Extend the missing-entity test: assert `FailureSummary` **contains** the entity name or “not found” message (not null/empty).
4. Optional: assert at least one Error diagnostic message mentions the missing target.

## Verification

- [ ] Missing-target rollback still works
- [ ] `FailureSummary` non-empty and mentions the target
- [ ] Analysis Error diagnostics include the message
- [ ] Build + evolution tests green

## Output

- `DomainEvolution.cs` (+ maybe `EvolutionResult` only if needed)
- Test update
- Summary: `../agent-summaries/vs-s0-fail-loud-surface-eval-errors-summary.md`

## Out of Scope

- Child stage/property missing (#0.1b)
- Remaining ApplyTo coverage (#0.1c)
- MCP fingerprint changes

## Status tracking

**Claimed by:**  
**Started:**  
**Notes / Blockers:**
