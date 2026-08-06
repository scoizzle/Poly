# DAS W1.1 — Domain catalog design

**Wave:** W1 · **Queue:** [`das-README.md`](./das-README.md)  
**Future state:** [`../domain-analysis-future-state.md`](../domain-analysis-future-state.md) §3.2, §4  
**Difficulty:** Medium (design)  
**Status:** `[ ]`  
**Prereq:** W0 gate  

## Objective

Lock the shape of the **single catalog** before coding: slices, keying, SA ownership, migration dual-write policy. Output is a short design note in-repo (extend future-state §4 or `docs/plans/das-catalog-design.md`).

## Tasks

- [ ] W1.1.1 Enumerate every current publisher of name→member maps (DTLM, RLM, MTI, ARM, ESM.StageByName, etc.) and their keys.
- [ ] W1.1.2 Propose `DomainCatalogMetadata` (or final name): fields, key (domain node), immutability.
- [ ] W1.1.3 Specify which APIs own SA fallthrough (`TryResolveAction` only).
- [ ] W1.1.4 Specify subscription plan identity (stage node keys) vs catalog (names).
- [ ] W1.1.5 Dual-write plan for one release if needed; deletion criteria for old bags.
- [ ] W1.1.6 List consumer migration order: lookup extensions → runtime → MCP → evolution → lowering.

## Acceptance criteria

- [ ] Design note committed under `docs/plans/` and linked from future-state or this suite.
- [ ] No production code required; optional spike only.
- [ ] Explicit “out of scope” list (standalone Domain null, live evolution overlay).

## Progress notes

(empty)
