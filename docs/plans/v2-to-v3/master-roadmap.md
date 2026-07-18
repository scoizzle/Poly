# DomainModeling product roadmap (milestones)

**Status:** Active (milestones index)  
**Last Updated:** 2026-07-17
**Purpose:** High-level milestone status only.
**Day-to-day work:** [`domainmodeling-next-phase.md`](domainmodeling-next-phase.md)
**Phase 1a archive:** [`dsl-sync-toward-phase1.md`](dsl-sync-toward-phase1.md)

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

| Priority | Work | Where | Status |
|----------|------|--------|--------|
| 1 | **Phase 2: Spawn-and-wire** | [`domainmodeling-next-phase.md`](domainmodeling-next-phase.md) | **Complete** |
| 2 | P2.1 Create→Link runtime | `DomainEntityInstance.CreateChildInstance` + `CreateEntityInstance.RelationshipName` | Done |
| 3 | P2.2 E-create DSL (`create`/`create in`) | `PolyDslParser` + `DomainDslPrinter` + grammar | Done |
| 4 | P2.3 Dogfood golden path | `Dogfood_CreateInDSL_SubscriptionFires` + `ApplyDsl_WithCreateInAndSubscription_Succeeds` | Done |
| 5 | P2.4 E-entry | OnEntry/OnExit DSL print/parse | Done |
| 6 | P2.5 Multi-stage when-list | Parser extension | Done |

---

## Archived migration material

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
