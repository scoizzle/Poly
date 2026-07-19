# DomainModeling product roadmap (milestones)

**Status:** Active (milestones index)  
**Last Updated:** 2026-07-18  
**Purpose:** High-level milestone status only.  
**Day-to-day work:** [`dsl-query-surface.md`](dsl-query-surface.md) **§15** · [`simple-agent-tasks/qe-README.md`](simple-agent-tasks/qe-README.md)  
**Phase 1a archive:** [`dsl-sync-toward-phase1.md`](dsl-sync-toward-phase1.md)  
**Phase 2 archive (complete):** [`domainmodeling-next-phase.md`](domainmodeling-next-phase.md)

---

## Current product path

```text
Domain (immutable) → DomainEvolution → DomainExpression lower → Syntax AST
  → analysis / node replacement → DirectVmAbiEmitter → VM
MCP / direct API as thin consumers
```

- **No** product µop / primitive IR path  
- **No** V2 (`Poly.Data.Modeling` deleted)  
- **M2** first-consumer vertical slice (structure + policy API + MCP policy) **Done**  
- **Suite baseline:** **1385** (after Q1'''''' `25a79ec` — Rel exists on nav fixed)

---

## Milestones

| Milestone | Status |
|-----------|--------|
| **M1 — Foundation** | ✅ Evolution, proofs, analysis gate |
| **M2 — First consumer** | ✅ Done 2026-07-12 — structure + Person policy + MCP add/evaluate; suite green |
| **M3 — V2 freeze** | ✅ Done |
| **M4 — V2 delete** | ✅ Done |

---

## What next

| Priority | Work | Where | Status |
|----------|------|--------|--------|
| 1–8 | Phase 2–3, RT, SA, E0+E1, E2.1 | phase3 · effect-surface | **Complete** |
| 9 | **Q0 → Q1′ authoring** | dsl-query-surface | **Complete** `959c6e7` |
| 10 | **Q1′′′ residual nits** | query §11 | **Partial** `3c99221` |
| 11 | **Q1′′′′ DMREL001 + test honesty** | query §12 | **Complete** `76568a3` |
| 12 | **Q1''''' hygiene** | query §13 | **Complete** `514e21c` |
| 13 | **Q1'''''' Rel exists fix** | query §14 | **Complete** `25a79ec` — N1 nav names accepted for `Rel exists`; goldens green |
| 14 | ▶ **Q3′ decision** | query **[§15](dsl-query-surface.md)** | **Next** — implement any/all/count **or** explicit non-goal “no collection quantifiers in v1” |
| 15 | **§15 low hygiene** | query §15 Q1'''''''.1–.5 | **Pull** — guide nested path-prefix note; dead code; owned story; test placement |
| 16 | **Optional RT eval** related policies | query §15 Q1'''''''.8 | **Pull** — store/VM path when product needs evaluate |
| 17 | E3 / effect-micro / L\* / events | effect · expansion · phase3 §6d | **Pull / post–P3 / never** |

### Agent pick (one line)

```text
CURRENT: Q3′ by dogfood pain OR write explicit non-goal in query success criteria
THEN:    §15 low hygiene (optional)
PULL:    RT eval related policies; E3b; link DSL; L*; host I/O
```

**Honest product claim today:** Path-prefix, `where`, and **`Rel exists` on N1 navs** are **authorable** (parse/print/apply/export) with analysis guards (**DMREL001** on many; unknown-rel; target body props). Guide: **not RT-evaluated** for related expressions. Collection quantifiers (**Q3′**) still missing or non-goal TBD.

---

## Archived migration material

**[`../archive/v2-to-v3-migration/README.md`](../archive/v2-to-v3-migration/README.md)**

Do **not** claim WS8 Phase B, WP7 port, or µop pipeline work from archive.

---

## Quality bar (still applies)

| Focus | Meaning |
|-------|---------|
| Correctness | Analysis-gated evolution, honest diagnostics, VM-primary when claimed |
| Composition | Direct API + thin MCP |
| Tests | Names match assertions; product forms covered under **analysis** |
| Query honesty | Cross-entity **reads** legal; **writes** banned; authoring ≠ RT eval |

Platform map: [`docs/CORE.md`](../../CORE.md).
