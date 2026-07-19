# DomainModeling product roadmap (milestones)

**Status:** Active (milestones index)  
**Last Updated:** 2026-07-18  
**Purpose:** High-level milestone status only.  
**Day-to-day work:** [`dsl-query-surface.md`](dsl-query-surface.md) **§14** · [`simple-agent-tasks/qe-README.md`](simple-agent-tasks/qe-README.md)  
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
- **Suite baseline:** **1382** (after Q1''''' `514e21c`)

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
| 12 | **Q1''''' hygiene** | query §13 | **Complete** `514e21c` — guide authoring-only, nested-where ban, unknown-rel, body→target validation |
| 13 | ▶ **Q1'''''' Rel exists fix** | query **[§14](dsl-query-surface.md)** | **Next** — `Exists(PropertyAccess(nav))` must accept **relationship** names (N1 nav is not an entity property); apply_dsl golden `assignee exists` |
| 14 | **Q3′ any/all/count** | dsl-query-surface | **Pull** after §14 high item (or honest non-goal) |
| 15 | **Optional RT eval** related policies | query §14 Q1''''''.6 | **Pull** |
| 16 | E3 / effect-micro / L\* / events | effect · expansion · phase3 §6d | **Pull / post–P3 / never** |

### Agent pick (one line)

```text
CURRENT: Q3′ by pain OR honest non-goal
THEN:    Q1''''''.5 hygiene
PULL:    RT eval related policies; E3b; link DSL; L*; host I/O

**All planned query-surface slices shipped.** Q1′ through Q1'''''' lines: authoring path + analysis rejections for common mistakes. No blocking issues remain for Q1′ existentials and path-prefix forms.
```

**Honest product claim today:** Path-prefix / `where` / `Rel exists` related forms are **authorable** (parse/print/apply/export) with **DMREL001** on many and N1 nav names accepted in relationship existence checks. Guide states **not RT-evaluated**.

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
| Tests | Names match assertions; cover product forms under **analysis**, not parse-only |
| Query honesty | Cross-entity **reads** legal; **writes** banned; authoring ≠ RT eval |

Platform map: [`docs/CORE.md`](../../CORE.md).
