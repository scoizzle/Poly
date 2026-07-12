# DomainModeling product roadmap (milestones)

**Status:** Active (milestones index)  
**Last Updated:** 2026-07-12  
**Purpose:** High-level milestone status only.  
**Day-to-day work:** [`simple-agent-tasks/vs-README.md`](simple-agent-tasks/vs-README.md)  
**Slice status:** [`vertical-slice-finish-plan.md`](vertical-slice-finish-plan.md)

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

| Priority | Work | Where |
|----------|------|--------|
| 1 | Post-M2 MCP evaluate multi-property sample | `vs-pm2-evaluate-policy-sample-bag.md` |
| 2 | add_policy → evaluate_policy affordance | `vs-pm2-add-policy-evaluate-affordance.md` |
| 3 | Optional remove-zero-match | `vs-s0-fail-loud-remove-zero-match.md` |
| 4 | Naming cleanup (drop V3*) | [`../post-v2-delete-naming-cleanup.md`](../post-v2-delete-naming-cleanup.md) |
| 5 | First effect / relationships | Pull-only (Slice 4/5) |
| 6 | T2 dogfood | Trust ADR |

---

## Archived migration material

Completed/superseded workstreams, WP/ws micro-tasks, and µop-era WS8 docs:

**[`../archive/v2-to-v3-migration/README.md`](../archive/v2-to-v3-migration/README.md)**

Do **not** claim WS8 Phase B, WP7 port, or µop pipeline work from archive.

---

## Quality bar (still applies)

| Focus | Meaning |
|-------|---------|
| Correctness | Analysis-gated evolution, honest diagnostics, VM-primary policy when claimed |
| Composition | Direct API + thin MCP |
| Tests | Direct API first; MCP smoke second |
| Natural names | Prefer what-it-is over V3 migration labels (see naming cleanup) |

Platform map: [`docs/CORE.md`](../../CORE.md).
