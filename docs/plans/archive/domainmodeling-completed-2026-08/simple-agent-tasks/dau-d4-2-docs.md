# Micro-Task: DAU.D4.2 — CORE + README + inventory sync

**Suite:** [`dau-README.md`](dau-README.md) **#D4.2**  
**Parent:** [`../domain-analysis-unification.md`](../domain-analysis-unification.md) §5 Phase 4  
**Difficulty:** Small  
**Prereq:** **D3.7**  
**Status:** `[x]` — §14 inventory updated; optional CORE/README spot-check

## Objective

Docs match the tree after D3: Analysis owns domain facts including storage/transport; Lowering = DE/policy/true lower; packs refine via DomainAuthoringContext; RestApi consumes Transport at emit.

## Exact Steps

1. Update `docs/CORE.md` only if domain analysis / lowering placement lines are stale (minimal diff).  
2. Update `Poly/DomainModeling/README.md` Analysis vs Lowering table.  
3. Update `docs/domainmodeling-capability-inventory.md` §5.1/§5.2: storage/transport on domain pipeline; codegen emit-first.  
4. Update parent plan status header if Phase 3 complete.  
5. No product code changes.

## Definition of Done

- [ ] Inventory and DomainModeling README describe current homes  
- [ ] No “storage codegen-only” false claims  
- [ ] RestApi described as transport consumer if mentioned  
- [ ] `dau-README` D4.2 `[x]`  

## Review feedback (2026-07-25) — why reopened

Inventory still says storage/transport are “codegen today → DAU D3” and topo/agg/beh are “mid-migration wrappers under Lowering” — **stale** after D1 + partial D3. DoD “no storage codegen-only false claims” not met. Re-sync after Phase 3 truly done (or update now for what already landed: Storage/Transport on domain pipeline).

## Status tracking

**Claimed by:**  
**Started:**  
**Notes / Blockers:** REOPEN — inventory stale