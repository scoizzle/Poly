# V2 → V3 Domain Modeling Port — Master Roadmap

**Status**: Active  
**Last Updated**: 2026-07-10  
**Purpose**: Canonical entry point for execution planning.  
**Related**:
- **`docs/plans/v2-to-v3/v3-completion-plan.md`** — **implementation gaps + work packages (WP0–WP9)**; use this for day-to-day execution order
- `docs/decisions/2026-v2-to-v3-domain-modeling-port.md` (strategic plan)
- `docs/decisions/2026-core-engineering-principles.md`
- `docs/decisions/2026-05-31-evolution-layer-design.md`
- `docs/decisions/2026-06-08-vm-as-canonical-semantics.md`
- `docs/decisions/2026-06-08-domain-lowering-boundary.md`
- `docs/plans/v2-to-v3/spikes/first-v3-consumer.md` (named M2 consumer + quality bar)
- `docs/plans/v2-to-v3/spikes/mcp-guiding-principles.md` (MCP implementation principles from research)

---

## Quality bar (how we build M2+)

These focus areas govern design and review for the V2→V3 cutover. They sit on top of the six core principles in AGENTS.md.

| Focus | Meaning in practice |
|-------|---------------------|
| **System correctness** | Analysis-gated evolution, honest rollback, diagnostics that match the model, DomainExpression that evaluates correctly on the VM when runtime truth is required. |
| **Robustness via composition** | Composition on the **direct API** (small ops + batch `Apply`). MCP **curates** tools for agents — not a 1:1 mutator mirror, not a single opaque bag. |
| **MCP + direct API as guiding light** | **MCP** is the near-term agent scenario. The **direct domain API** is the contract into DomainModeling / Syntax / Interpretation. Features without a call site on that path wait. Tool design follows `spikes/mcp-guiding-principles.md`. |
| **Tests help** | Prefer TUnit on the **direct API** (and VM eval) as the primary net; MCP smoke / agent-task evals reuse the same scenarios. Behavioral coverage over structure-only asserts. |
| **Code that reads naturally** | Names and fluent shapes that describe *what happens* (`Evolve().AddEntity(...)`, clear tool names). Pattern taxonomy and V2 intent-bag shapes are not the default. |

**Layering (fixed):**

```
MCP tools + workspace/session (thin consumer)
        →  Model-optimized DomainModeling API (single Evolve path + queries)
        →  Domain / analysis / Syntax / VM
                    ▲
         tests (core API; optional MCP types for session)
```

- **Workspace/session** lives in **MCP only**.  
- **One evolution system** on immutable roots.  
- Sugar layers appear as we build the vertical slice — not a pre-designed second façade.

**MCP rewrite constraints (summary — full list in the spike):**

1. Thin adapter over direct API  
2. Curate tool count (~10–25 for M2, not ~80)  
3. Outcome + atomic tools as appropriate; composition server-side  
4. Descriptions/schemas are agent UX; flat args  
5. Concise, high-signal responses + affordances  
6. Recoverable errors (diagnostics + next steps)  
7. Session/revision discipline; destructive ops honest  
8. Eval-driven improvement after ship

---

## Strategic reality (July 2026)

### Zero product consumers of V2

**V2 (`Poly/Data/Modeling`) has no product consumers today.** Nothing outside the repo (and no live agent/MCP deployment we depend on) requires V2 behavior, DTOs, or mutators.

What still *touches* V2 is **in-repo only**:

| Area | Role |
|------|------|
| `Poly/Data/Modeling/**` | Legacy implementation |
| `Poly.Mcp/DomainTools.cs` | V2-shaped prototype — **not** a live consumer constraint |
| `Poly.Benchmarks/DomainModeling/**` | Demos against V2 |
| `Poly.Tests/Data/Modeling/**`, some integration tests | Regression net for V2 |

**Implication:** This is still the low-risk rewrite window. Success is **not** “migrate live MCP without breakage.” Success is:

1. **V3 is the only modeling stack we invest in**
2. **First real consumer** = **MCP on a direct V3 domain API** (see spike) — rewrite, not a compatibility shim
3. **V2 is frozen, then deleted** when that consumer works

Do **not** expand V3 to full V2 feature parity before that path works. Do **not** treat the current V2-shaped MCP tools as sacred API.

### Interpretation is ready enough

| Capability | Status |
|------------|--------|
| Direct AST→ABI (`DirectVmAbiEmitter`) | ✅ Sole compile path |
| VM sole engine (no tree-walker) | ✅ |
| DomainExpression → Syntax AST | ✅ |
| Domain → generic AST only | ✅ |
| Perf harness competitive | ✅ |
| Statement-level debugger | ✅ |

Further Interpretation work is **on-demand** when a V3 consumer forces a gap.

**Plans hygiene:** Pre-direct-ABI Interpretation plans live under `docs/plans/archive/interpretation/`. Do not implement from the archive.

---

## Success criteria (replace old “parity” language)

| Milestone | Done when |
|-----------|-----------|
| **M1 — Foundation** | ✅ Evolution layer real; proofs; audit |
| **M2 — First V3 consumer** | **1–2 entity concepts fully working** on V3: direct path + curated MCP + tests (author/analyze/query; optional policy+records). **Sharp cliff** off V2 MCP tools. No V2-style export/import requirement (DSL later). |
| **M3 — V2 freeze** | No new V2 features; **aggressive port/delete** of V2 tests underway |
| **M4 — V2 delete** | `Poly/Data/Modeling` removed; demos/tests/MCP on V3 only |

**Named consumer:** `spikes/first-v3-consumer.md` — MCP + direct API (not CLI-first, not demo-only).

**Non-goals until M2:** Actor port, full rule-system port, contract-interface matrix for its own sake, V2 intent adapter for “compatibility,” full V2 tool-count parity.

---

## Core rules for agents

1. Consult decisions first (AGENTS.md).
2. Check this roadmap + workstream before claiming work.
3. Summaries go in `agent-summaries/`; orchestrators update this file.
4. Small verifiable increments; domain model is the key artifact.
5. **No new V2 code** except critical bugfixes needed to keep the tree building while M2 lands.
6. Prefer **delete V2** over long dual maintenance.
7. **Correctness, composition, natural code** — every M2 PR should leave the direct API more trustworthy and more readable, not denser.
8. **MCP is thin** — domain logic lives in `Poly.DomainModeling` (and helpers next to it), not in `Poly.Mcp` tool bodies.

See **[orchestration-guide.md](orchestration-guide.md)**.

---

## High-level phases (reframed)

| Phase | Focus | Status |
|-------|-------|--------|
| **Phase 1** | Evolution foundation + proofs + expressiveness audit | **Complete** (hygiene: WS4/WS6 only) |
| **Phase 2** | Capabilities the **first V3 consumer** needs (e2e eval, codegen, traces) | **Active** — pull by M2, not V2 parity |
| **Phase 3** | **First consumer on V3** + freeze V2 | **Not started** — highest leverage |
| **Phase 4** | Expressiveness as **pulled by** that consumer / next scenarios | Design ready; no speculative ports |
| **Phase 5** | **Delete V2** + doc cleanup | After M2 proven |

---

## Phase 1 (complete — do not reopen)

- `DomainEvolution.Apply` / `Evolve()` + `DomainMutationContext`
- 66 `DomainChange` subtypes + fluent builder
- Analysis gate + rollback
- NodeId continuity + incremental analysis
- WS5 proofs (PersonLifecycle + Library)
- WS7 expressiveness audit (living)

| Workstream | Status |
|------------|--------|
| WS1 Applicator | **Complete** |
| WS5 Proofs | **Complete** |
| WS7 Audit | **Complete** |
| WS4 Trace polish | Optional hygiene |
| WS6 Doc hygiene | Ongoing (this update) |

---

## Phase 2 (support MCP + direct API — not “match V2”)

| Workstream | File | Status |
|------------|------|--------|
| **WS8** | `workstreams/ws8-analysis-unification-and-lowering.md` | **Active** |

### Done

- DomainExpression → Syntax AST
- Shared analysis substrate
- VM execution good enough for proofs
- `PolicyEvaluator` path exists

### Open — only if M2 needs it

Ordered by pull from **MCP + direct API** (`spikes/first-v3-consumer.md`):

1. **Direct domain API surface** — thin, composable, naturally named ops over `Evolve`/`Apply` + query projections (implementation work under Phase 3; may live next to DomainModeling, not only in MCP)
2. **E2E DomainExpression → VM** — when policy/guard tools need runtime truth (`ws8-e2e-policy-vm-eval.md`)
3. **Trace quality for agents** — WS4 guide + gaps dogfood hits (`ws4-agent-trace-reading-guide.md`)
4. **Smoke matrix** for lowering regressions (`ws8-domainexpression-lower-smoke-matrix.md`)
5. **Contract / program generation** — only if tools emit C#/interfaces (`ws8-inventory-v2-contract-interface-rules.md` then minimal surface)

**Non-goals:** full analyzer parity, full V2 lowering feature matrix, µop/IR work, dual engines.

---

## Phase 3 (MCP + direct API + freeze V2)

**Named:** rewrite MCP on V3 with a **direct domain API** as the real contract. See `spikes/first-v3-consumer.md`.

| Layer | Responsibility |
|-------|----------------|
| Direct API | Evolve, analyze, query, optional lower/eval — **composable, tested first** |
| MCP | Sessions, DTOs, tool metadata, **curated agent UI** — see `spikes/mcp-guiding-principles.md` |

**Phase 3 exit (M2 + M3):**

- [x] First consumer **named** (MCP + direct API)
- [x] MCP **guiding principles** documented (research + Poly bar)
- [ ] Direct API uses only `Poly.DomainModeling` (+ Syntax/Interpretation as needed)
- [ ] MCP tools call that API only (no `Poly.Data.Modeling` mutators)
- [ ] Tool surface follows principles (curated count, descriptions, concise responses, affordances, recoverable errors)
- [ ] Multi-step evolve with analysis error → rollback + usable diagnostics
- [ ] TUnit covers direct-API happy path + failure/rollback
- [ ] Policy/calculation on VM when a tool needs runtime truth
- [ ] **V2 freeze declared** in this roadmap + AGENTS.md note: no new V2 features
- [ ] Inventory of remaining V2 references (tests, demos, old DomainTools) with deletion plan

**Do not:** build a long-lived V3→V2 adapter “for MCP continuity.”  
**Do not:** put domain rules only inside MCP tool methods.  
**Do not:** port `DomainTools.cs` tool-for-tool (~80 mutator mirrors).

---

## Phase 4 (pull-only expressiveness)

WS7 remaining gaps (Actor, rule-composed policies, etc.) ship **only** when the live MCP/direct path (or the next consumer) requires them.

Design refs: `docs/decisions/2026-06-phase4-dynamic-calculation-and-readonly-navigation.md`, WS7 audit.

---

## Phase 5 (delete V2)

- Remove or isolate `Poly/Data/Modeling`
- Rewrite/delete V2-only tests, demos, `Poly.Mcp/DomainTools.cs` V2 paths
- Update AGENTS.md placement rules if needed
- Celebrate end of dual stack

---

## Immediate starting point

**Authoritative gap list + packages:** [`v3-completion-plan.md`](v3-completion-plan.md)

| Order | Package | Status |
|-------|---------|--------|
| WP1 | V3 builtins + sever PolicyEvaluator V2 | ⬜ start here |
| WP2 | Direct API queries + evolve façade docs/tests | ⬜ |
| WP3 | Correctness net (rollback + policy VM) | ⬜ |
| WP4 | Curated MCP rewrite | ⬜ |
| WP5 | Runtime truth polish | pull |
| WP6–8 | Freeze → port demos → delete V2 | after M2 |
| WP9 | Actor / rules / contract gen / visual | pull only |

### Orchestrator

1. ~~Name the first V3 consumer~~ → **Done**
2. ~~MCP principles~~ → **Done**
3. ~~Completion plan / gap inventory~~ → **Done** (`v3-completion-plan.md`)
4. Drive **WP1 → WP4** micro-tasks; WP5/WS8 only if tools need eval
5. Freeze relative to M2 green

### Executor

1. Pick **`wp1-*` then `wp2-*` then `wp3-*` / `ws8-e2e-*` then `wp4-*`** from `simple-agent-tasks/`.
2. Skip superseded `ws1-*` / `ws2-*` / old `ws3-add-*`.
3. Do not add DomainChange types without a direct-API call site + test.
4. Do not port V2 DomainTools 1:1.

### Recommended sequence

```
1. WP1 — builtins + no V2 under DomainModeling
2. WP2 — query projections + happy-path evolve tests
3. WP3 — rollback suite + policy VM e2e
4. WP4 — MCP session/overview/evolve (curated) + retire V2 tools path
5. Dogfood → refine descriptions
6. WP6 freeze → WP7 demos → WP8 delete V2
7. WP9 only when consumer pulls
```

---

## Readiness checklist

| Item | Status |
|------|--------|
| Evolution layer real | ✅ |
| Proofs / audit | ✅ (WS7 living; lowering notes partially stale — see completion plan §2) |
| DE → AST / VM ready enough | ✅ (pass + VM tests exist) |
| **Zero V2 product consumers** | ✅ |
| First V3 consumer named | ✅ |
| MCP guiding principles | ✅ |
| **V3 completion plan (gaps + WPs)** | ✅ |
| WP1 builtins / no V2 in DomainModeling | ⬜ |
| WP2 direct query API | ⬜ |
| WP3 test matrix | ⬜ |
| WP4 MCP on V3 (M2) | ⬜ |
| V2 frozen | ⬜ |
| V2 deleted | ⬜ |

**Bottom line:** Foundation is done. Execution order is **WP1→WP4** in `v3-completion-plan.md`. Win condition is **MCP + direct API on V3 + delete V2**, not V2 parity.

---

## Historical notes

Older plan language assumed live MCP migration and dual maintenance for agents already on V2. That assumption is **withdrawn**. July 2026 further **named** the first consumer as MCP on a direct domain API (not CLI/demo-first). Agent summaries and superseded micro-tasks from the WS1 era remain for provenance only.
