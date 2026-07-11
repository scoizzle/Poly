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

### V3 is the only modeling stack

**V2 (`Poly/Data/Modeling`) has been deleted** (M4). Product path:

| Area | Role |
|------|------|
| `Poly/DomainModeling/**` | Immutable domain model + evolution + queries + bootstrap |
| `Poly.Mcp/Tools/` + `Sessions/` | Curated MCP consumer (V3 only) |
| `Poly.Tests/DomainModeling/**`, `Poly.Tests/Mcp/` | Correctness net for V3 |
| `Poly/DomainModeling/Examples/` | V3 demos |

**Implication:** Do not reintroduce V2. Invest only in V3 (direct API + MCP). Expand expressiveness when a real consumer pulls it — not for parity theater.

Do **not** treat archived V2-shaped plans or old DomainTools designs as sacred API.

---

## 🔴 V2 Freeze → Delete (2026-07-10)

**Freeze (M3):** Declared in AGENTS.md — no new V2 investment.  
**Delete (M4):** ✅ **`Poly/Data/Modeling` removed** from the tree; V2 tests, V2 benchmark demos, and `Poly.Mcp/DomainTools.cs` removed. Product path is V3-only (`Poly/DomainModeling` + V3 MCP tools).

Do **not** reintroduce `Poly/Data/Modeling`. Demos live under `Poly/DomainModeling/Examples/` (and V3 MCP) as needed.

Historical inventory (pre-delete, for provenance only): ~162 V2 core files + ~44 dependent test/demo/MCP files — **deleted rather than staged port.**

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
| **M3 — V2 freeze** | ✅ **Done** — AGENTS + roadmap freeze; no new V2 work |
| **M4 — V2 delete** | ✅ **Done** — `Poly/Data/Modeling` removed; V2 tests/demos/`DomainTools` gone; V3-only product path |

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
| **Phase 1** | Evolution foundation + proofs + expressiveness audit | **Complete** |
| **Phase 2** | Capabilities the first V3 consumer needs (e2e eval, codegen, traces) | **Pull** — policy VM e2e still open (WP5) |
| **Phase 3** | First consumer on V3 + freeze V2 | **Complete** — WP1–WP4 + WP6 |
| **Phase 4** | Expressiveness as pulled by consumers | Design ready; no speculative ports |
| **Phase 5** | Delete V2 + doc cleanup | **Complete** — V2 tree removed (WP7/WP8 leapfrog delete) |

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
- **Direct domain API surface** — queries, evolve façade, bootstrap factory, documented in DomainModeling README ✅
- **Curated MCP on V3** — session/query/evolve tools (11 tools, structured results, affordances) ✅

### Open — only if M2 needs it

Ordered by pull from **MCP + direct API** (`spikes/first-v3-consumer.md`):

1. ~~**Direct domain API surface** — thin, composable, naturally named ops over `Evolve`/`Apply` + query projections~~ ✅ **Done**
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
- [x] Direct API uses only `Poly.DomainModeling` (+ Syntax/Interpretation as needed)
- [x] MCP tools call that API only (no `Poly.Data.Modeling` mutators)
- [x] Tool surface follows principles (curated count, descriptions, concise responses, affordances, recoverable errors)
- [x] Multi-step evolve with analysis error → rollback + usable diagnostics
- [x] TUnit covers direct-API happy path + failure/rollback
- [ ] Policy/calculation on VM when a tool needs runtime truth (WP5 — pull)
- [x] **V2 freeze declared** in this roadmap + AGENTS.md note: no new V2 features
- [x] Inventory of remaining V2 references (tests, demos, old DomainTools) with deletion plan

**Do not:** build a long-lived V3→V2 adapter “for MCP continuity.”  
**Do not:** put domain rules only inside MCP tool methods.  
**Do not:** port `DomainTools.cs` tool-for-tool (~80 mutator mirrors).

---

## Phase 4 (pull-only expressiveness)

WS7 remaining gaps (Actor, rule-composed policies, etc.) ship **only** when the live MCP/direct path (or the next consumer) requires them.

Design refs: `docs/decisions/2026-06-phase4-dynamic-calculation-and-readonly-navigation.md`, WS7 audit.

---

## Phase 5 (delete V2) — complete

- [x] Remove `Poly/Data/Modeling`
- [x] Remove V2-only tests, demos, `Poly.Mcp/DomainTools.cs`
- [x] V3 demos under `Poly/DomainModeling/Examples/` as needed
- Remaining: keep docs/AGENTS freeze language until any leftover references are scrubbed; do not reintroduce V2

---

## Immediate starting point

**Authoritative gap list + packages:** [`v3-completion-plan.md`](v3-completion-plan.md)  
**Micro-tasks:** [`simple-agent-tasks/README.md`](simple-agent-tasks/README.md)

| Order | Package | Status |
|-------|---------|--------|
| WP1 | V3 builtins + sever PolicyEvaluator V2 | ✅ **Done** |
| WP2 | Direct API queries + happy-path tests | ✅ **Done** |
| WP3 | Correctness net (rollback suite) | ✅ **Done** |
| WP4 | Curated MCP rewrite | ✅ **Done** |
| WP6 | V2 freeze declaration | ✅ **Done** |
| WP7 | Aggressive V2 test/demo port | ✅ **Superseded** — deleted rather than staged port |
| WP8 | Delete V2 | ✅ **Done** (tree removed; commit if still unstaged) |
| **WP5 / WS8** | Runtime truth polish | 🟡 **In Progress residuals** — MCP eval honesty + domain-attached policy test (`ws8-README.md`) |
| WP9 | Actor / rules / contract gen / visual | pull only |

**Executor rule:** M1–M4 complete. Finish **WS8 In Progress residuals first** ([`ws8-README.md`](simple-agent-tasks/ws8-README.md): MCP `evaluate_policy` honesty, then domain-attached policy test). Skip superseded WP7/WP8 micro-tasks.

### Orchestrator

1. ~~Name consumer / MCP principles / completion plan / WP1–WP4 / freeze / delete~~ → **Done**
2. Drive **WP5** micro-tasks (policy e2e, DE smoke; optional MCP evaluate tool)
3. Ensure V2 purge is **committed** if still only in the working tree

### Executor

1. Pick from **Next** table in `simple-agent-tasks/README.md` (top first).
2. Skip superseded `wp7-*` / `wp8-delete-*` / old `ws1-*` foundation tasks.
3. Do not reintroduce `Poly/Data/Modeling`.
4. No speculative Actor/contract gen without a consumer.

### Recommended sequence

```
1–4. WP1–WP4   ✅ Done (M2)
5.   WP6 freeze ✅ Done (M3)
6.   WP7–WP8    ✅ Done via full delete (M4)
7.   WP5        ← next: ws8-e2e-policy-vm-eval → DE smoke → optional MCP eval
8.   WP9        only when consumer pulls
```

---

## Readiness checklist

| Item | Status |
|------|--------|
| Evolution layer real | ✅ |
| Proofs / audit | ✅ (WS7 living) |
| DE → AST / VM ready enough | ✅ |
| **Zero V2 product consumers** | ✅ |
| First V3 consumer named | ✅ |
| MCP guiding principles | ✅ |
| **V3 completion plan (gaps + WPs)** | ✅ |
| WP1–WP4 M2 authoring path | ✅ **Done** |
| V2 frozen | ✅ **Done** |
| V2 deleted | ✅ **Done** (2026-07-10; verify commit on branch) |
| Policy / expression e2e productized | 🟡 Bare Policy→VM ✅; domain-attach + honest MCP eval still open |

**Bottom line:** Cutover complete (M1–M4). WS8 foundation Done; **close review residuals** (MCP honesty, domain-attached policy test) before WP9.

---

## Historical notes

Older plan language assumed live MCP migration and dual maintenance for agents already on V2. That assumption is **withdrawn**. July 2026 further **named** the first consumer as MCP on a direct domain API (not CLI/demo-first). Agent summaries and superseded micro-tasks from the WS1 era remain for provenance only.
