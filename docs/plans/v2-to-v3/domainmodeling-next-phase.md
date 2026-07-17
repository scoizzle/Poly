# DomainModeling — Next Phase Plan

**Date:** 2026-07-17  
**Status:** Phase 1a-runtime (BR.4.4 / Option A) **shipped** (`8f46f05`)  
**Predecessor:** Phase 1a closed (`dsl-sync-toward-phase1.md`); PCA (`2dd5a68`)  
**Decision:** User chose **Option A** (instance-graph honesty) over dogfood-first (B)

---

## Phase framing

| Frame | Status |
|-------|--------|
| Phase 1a | Done |
| **Phase 1a-runtime (instance graph)** | **Done** — IG.0–IG.3 shipped (`8f46f05`) |
| Phase 1a′ dogfood | Optional parallel later |
| Phase 1b (Slice E) | Pull-only |

---

## Phase 1a-runtime — Instance graph (BR.4.4)

### Goal

Subscription fan-out matches **instance** topology, not entity-type only.

### Design (IG.0)

- `DomainInstanceStore` holds adjacency list: `(relationshipName, source, target)` with **reference equality**
- Public API: `Link`, `Unlink`, `IsLinked`
- `NotifyTransition` requires `IsLinked(rel, subscriber, transitioned)` in addition to type/stage match
- `LinkRelationshipEffect` / `UnlinkRelationshipEffect` in CallAction: target must be `PropertyAccess` whose bag value is a `DomainEntityInstance` (or use store.Link API directly)

### Checklist

- [x] **IG.0** Link store shape on `DomainInstanceStore`
- [x] **IG.1** CallAction executes Link/Unlink effects
- [x] **IG.2** NotifyTransition filters by instance links
- [x] **IG.3** Golden 2×2 test + Unlink + Link-via-effect tests; existing subscription tests updated to `Link`
- [x] **IG.4** Committed (`8f46f05`)

### Exit criteria

- [x] Multi-instance fan-out correct by link (2×2 golden)
- [x] Type-level-only fan-out removed (require link)
- [x] Suite green
- [x] Work committed (`8f46f05`)

### Out of scope

Multi-hop paths, Any/All runtime, relationship lifecycle stages, MCP graph query, create-in auto-link (unless needed).
