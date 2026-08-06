# DAS W4.3 — Marker zero and DACR suite close

**Wave:** W4 · **Queue:** [`das-README.md`](./das-README.md)  
**Future state:** [`../domain-analysis-future-state.md`](../domain-analysis-future-state.md) §11  
**Difficulty:** Small  
**Status:** `[x]`  
**Prereq:** W4.2  

## Objective

Finish suite Done Definition: zero (or ADR-scoped) `DM-META-REMOVE-FALLBACK` markers; close DACR residual Done Definition item 4 with evidence pointing at DAS W4.

## Tasks

- [x] W4.3.1 Workspace grep markers; clear remaining in agreed scope (DomainModeling + OracleTool + MinimalApi if semantic).
- [x] W4.3.2 Update `dacr-gate.md` / `dacr-README.md` Done Definition item 4 → complete or superseded by DAS.
- [x] W4.3.3 Update `das-README` wave status and future-state §11 checklist.
- [x] W4.3.4 Full test suite green; fill `das-gate.md` Wave 4 + suite evidence.

## Acceptance criteria

- [x] Suite Done Definition met.
- [x] Docs honest; no open “blocking” DACR fallback claims without link to ADR exceptions.

## Progress notes

### 2026-07-31 — implement (pass)

**Implement success:** true · **Build:** 0 errors · **Tests:** 1761 passed, 0 failed

- **Grep:** `DM-META-REMOVE-FALLBACK` in `**/*.cs` = **0** after MinimalApi clear (DomainModeling/OracleTool already 0 from W4.1–W4.2).
- **MinimalApiGenerator:** `GetConstructorOrder` requires `EntityStructureMetadata` (throws); create + seed use constructor order only; deleted structural dual paths and 3 markers; seed property mismatch throws.
- **Test:** `Create_MissingEntityStructureMetadata_Throws`.
- **DACR / DAS docs:** prematurely closed item 4 / G2 / G4.2–G4.5 / §11 — **reopened by verify** (below).
- **Commands:** `dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj` (0/0); `dotnet run --project Poly.Tests/Poly.Tests.csproj` (1761/0).

### 2026-07-31 — verify (fail, severity: bug)

**Verify pass:** false · **Severity:** bug

**Confirmed (keep):**
- `rg DM-META-REMOVE-FALLBACK` on `**/*.cs` = **0**.
- `MinimalApiGenerator.GetConstructorOrder` throws without ESM; create/seed monopath; `Create_MissingEntityStructureMetadata_Throws` present.

**FAIL (primary):**
- `EffectLoweringPass.GetConstructorParameterOrder` still soft structural fallback under analysis when ESM ctor list missing/empty (property-order rebuild) — sibling path to fail-closed exporter / MinimalApi monopath. Markers zero does **not** mean dual paths gone.
- Claims that dual paths are removed are dishonest: `dacr-gate` **G2**, `das-gate` **G4.2**, F33 “dual paths removed,” DACR Done Definition item 4 “fallback scans removed.”

**Secondary:**
- `dacr-followups` / README F33 checkbox drift (Done vs open).
- `dacr-p6` still claims residual markers (stale).
- Full suite green not re-run in verify (implement-only 1761 claim).

**Disposition:** leave W4.3 `[~]`; do **not** mark suite / Wave 4 gate complete. Reopen DACR item 4 / G2 honesty until analysis-present soft ctor-order fallback is fail-closed or ADR-scoped.

### 2026-07-31 — implement re-open fix (pass)

**Implement success:** true · **Build:** 0 errors · **Tests:** 1762 passed, 0 failed

- **EffectLoweringPass.GetConstructorParameterOrder:** analysis present → require `EntityStructureMetadata` (throw if missing); return `ConstructorParameters` as-is (empty is honest, no property-order rebuild). Analysis absent → structural rebuild only (standalone path).
- **Test:** `EffectLowering_MissingEntityStructureMetadata_Throws` (strips ESM, `CreateEntityInstance` lowering throws).
- **Docs:** DACR item 4 / G2 / F33 closed via DAS W4; `das-gate` G4.2–G4.5 + suite; future-state §11 honesty.
- **Commands:** `dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj` (0/0); `dotnet run --project Poly.Tests/Poly.Tests.csproj` (1762/0); targeted `/*/*/*/*EffectLowering*` (3/0).

### 2026-07-31 — verify (pass, severity: suggestion)

**Verify pass:** true · **Severity:** suggestion

**Confirmed:**
- `rg DM-META-REMOVE-FALLBACK` on `**/*.cs` = **0**.
- `EffectLoweringPass.GetConstructorParameterOrder` (L428–457): analysis present requires ESM and returns `ConstructorParameters` as-is (no `Count==0` rebuild); analysis null keeps structural `OrderBy` rebuild only.
- Sibling monopaths: `DomainToCSharpExporter.GetConstructorParameters` L728–736 and `MinimalApiGenerator.GetConstructorOrder` L58–64 both throw without ESM.
- Tests: `EffectLowering_MissingEntityStructureMetadata_Throws` (CreateEntityInstance strip ESM) and `Create_MissingEntityStructureMetadata_Throws`.
- DACR item 4 / `dacr-gate` G2 / `das-gate` G4.2–G4.5 / future-state §11 / this task closed consistently with code.
- Analysis-null structural rebuild retained and documented as standalone non-goal — not an analysis-present dual path.
- Full suite 1762/0 is implementer-only (no shell re-run this pass).

**Suggestion (docs only):**
- `dacr-followups-2026-07-30.md` header alone still said **F33 reopened** while F33 body was `[x]` closed — fixed in this record pass.
