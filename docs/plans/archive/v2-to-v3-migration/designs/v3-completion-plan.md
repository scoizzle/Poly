# V3 Completion Plan — Gaps, Work Packages, Execution Order

**Status:** Active (inventory)  
**Last Updated:** 2026-07-11  
**Purpose:** Gap inventory and WP0–WP9 history for finishing V3 as the only domain modeling stack.  
**Authority:** Complements `master-roadmap.md` (milestones). **Day-to-day remaining execution order:** [`vertical-slice-finish-plan.md`](vertical-slice-finish-plan.md) (one vertical slice fully implemented at a time). This doc retains **what was missing**, **WP history**, and **micro-task seeds**.  
**Related:**
- `spikes/first-v3-consumer.md` — named consumer
- `spikes/mcp-guiding-principles.md` — MCP design rules
- `workstreams/ws7-v3-expressiveness-audit.md` — expressiveness (partially stale; see §2)
- `workstreams/ws8-analysis-unification-and-lowering.md` — lowering/eval pull
- `docs/decisions/2026-v2-to-v3-domain-modeling-port.md`

---

## 1. North star (unchanged)

```
MCP tools (thin, curated)  →  Direct domain API (composable)  →  DomainModeling / Syntax / VM
                                      ▲
                                 tests + demos
```

| Focus | Rule |
|-------|------|
| Correctness | Analysis gate, rollback, honest diagnostics, correct VM eval when required |
| Composition | Rich ops on direct API; batch `Apply`; MCP composes server-side when useful |
| Guiding light | MCP + direct API pull all features |
| Tests | Direct API first; MCP smokes / agent tasks second |
| Natural code | Name for what it is; no V2 intent bags as primary surface |

**Win condition:** Agents and tests construct, evolve, query, and (when needed) evaluate domains **only** via V3; V2 is frozen then deleted. Not “full V2 feature list.”

---

## 1.1 Product decisions locked (2026-07-10)

| Topic | Decision |
|-------|----------|
| **M2 scope** | Get **1–2 entity concepts fully working** end-to-end on V3 (author + analyze + query + MCP + tests), then flush out the rest of the surface. Not “every tool before any depth.” |
| **Vertical slice (default)** | Prefer a lifecycle-shaped entity (e.g. Person or Order): entity + properties + stages + actions + at least one policy/guard expression path. Second entity only if needed to prove relationships. Exact names TBD in WP2/WP4. |
| **Export/import** | **Not** M2 target as V2-style JSON DTOs. Prefer a future **DSL-spec** as the portable form. Defer portable transfer until that design lands. |
| **Runtime subjects** | Tests may use **C# records**. Long-term entity instance representation (nested `Dictionary<string, object>` simulation, codegen C#, or other) is **owned by Interpretation** when domain entities lower — not invent a parallel story in DomainModeling/MCP. |
| **V2 cutover** | **Sharp cliff**: when V3 MCP path works, stop registering V2 tools; no dual-stack product period. |
| **V2 tests** | **Port aggressively** into V3 tests (or delete if redundant) — do not keep a large V2-only suite as a comfort blanket until M4. |
| **Direct API / workspace** | **Decided (2026-07-10)** — see §1.2 |

### 1.2 Direct API & workspace (decided)

**MCP is a consumer**, not the home of domain semantics. The product surface into the system is a **great, model-optimized API** on `DomainModeling` (and Interpretation as needed). MCP adapts that API for agents; it does not redefine the model.

| Layer | Owns | Does not own |
|-------|------|----------------|
| **DomainModeling (direct API)** | Immutable `Domain` graph; **single** evolution path (`DomainEvolution` / `Evolve` / `Apply` + analysis gate); **model-optimized queries/views** (overview, entity shape, analysis summary as domain concepts); bootstrap/builtins; policy/expression hooks tests use | Session IDs, revision counters, agent DTOs, tool descriptions, long-lived “workspace” handle as a *product* type |
| **Poly.Mcp** | **Workspace/session** (sessionId → current `Domain` + revision + last analysis); thin tool methods; envelopes, affordances, agent-oriented messages | Domain rules, a second mutation engine, reinvented query semantics |
| **Tests** | Prefer **DomainModeling** for correctness of evolve/query/eval. **May** reference `Poly.Mcp` public types to exercise session/workspace behavior | — |

**Implications:**

1. **Single evolution system** — only `DomainEvolution` / change application on immutable roots. No parallel “MCP mutate” or mutable domain graph. Efficiency = batch changes → one analysis gate → new root or discard (immutability already gives atomicity).
2. **Workspace is an MCP problem** — `DomainWorkspace` / session store lives under `Poly.Mcp` (or MCP-adjacent host types), not in DomainModeling core. That removes the “what is the core host type?” thrash.
3. **“Great API” = model-optimized view** — C# that reads in domain terms (`Evolve().AddEntity…`, `DomainQueries.Overview(domain)`), not MCP/JSON shapes and not REST bags. Optimize for fidelity to the domain model, not for protocol convenience.
4. **Sugar is discovered** — no big façade redesign up front. Start with EvolutionBuilder + query helpers; add scenario helpers only when a real slice proves repetition. Layers of abstraction after working code.
5. **Tests and MCP share the same evolve/query core**; MCP adds only session lifecycle. Tests that need session semantics depend on MCP public types deliberately.
6. **Human UI is a future peer consumer** of the same DomainModeling API (and can reuse MCP session *patterns*). A great MCP surface (capabilities, diagnostics, affordances, revision) is also good UX scaffolding — but the UI should not be forced to go only through the LLM. See `spikes/mcp-guiding-principles.md` § Agents and human UI.

**Strawman shapes (names flexible):**

```text
// Core — no workspace type
Domain d = DomainFactory.Create("Demo");
var result = new DomainEvolution(d).Evolve().AddEntity("Order").Apply();
d = result.Domain; // or keep prior root if rolled back
var overview = DomainQueries.Overview(d);

// MCP — workspace/session
session.Apply(evo => evo.AddEntity("Order")); // inside: DomainEvolution + bump revision on success
```

---

## 2. Current state (code reality, July 2026)

### 2.1 Already solid (do not rebuild)

| Area | Evidence |
|------|----------|
| Immutable core model | `Entity`, `Stage`, `Action`, `Property`, constraints, `Relationship`, `Event`, `ValueType`, `Policy` + `DomainExpression` |
| Effects catalog | Create, Publish, Transition, Assign, Composite, Conditional, InvokeAction, Link/Unlink, Delete, … |
| Evolution | `DomainEvolution.Apply` / `Evolve()` + **~66** `DomainChange` subtypes + large fluent `EvolutionBuilder` |
| Analysis | ~17 V3 analyzers on shared `Syntax.Analysis` substrate |
| Expression lower | `DomainExpressionLoweringPass` → Syntax AST |
| Expression → VM | Tests in `Poly.Tests/DomainModeling/Lowering/DomainExpressionVmExecutionTests.cs` (literals, arithmetic, comparisons, property access patterns) |
| Policy helpers | `PolicyEvaluator` (VM + LINQ compile paths exist; needs V3-only cleanup) |
| Hand fixtures | `DomainBuilder` + PersonLifecycle examples |
| Interpretation | Direct ABI VM ready enough — not the critical path |

### 2.2 Gaps that block “rest of V3” as a product

Ordered by **pull from M2** (not by theoretical completeness).

| ID | Gap | Severity for M2 | Notes |
|----|-----|-----------------|-------|
| **G1** | Model-optimized **query/bootstrap** not productized; evolve exists but needs docs + slice tests | **Blocker** | Workspace stays in MCP; core = EvolutionBuilder + queries + factory |
| **G2** | No V3 **bootstrap / built-in type catalog** | **Blocker** | V2 has `CanonicalBuiltInTypeCatalog`; MCP CreateSession depends on it. V3 must not import V2 for bootstrap |
| **G3** | **V2 leakage into V3** (`PolicyEvaluator` `using Poly.Data.Modeling`) | **Blocker** | V3 must compile/run with zero V2 dependency for product path |
| **G4** | Thin **test net** + V2 tests not yet ported | **Blocker** | Vertical slice tests + **aggressive port** of valuable V2 tests to V3 |
| **G5** | **MCP still V2-shaped** (~80 tools, mutators, intents) | **Blocker** for M2 | Rewrite; **sharp cliff** off V2 tools when V3 path works |
| **G6** | **Policy / DomainExpression e2e** not productized | High for slice | Tests with **C# records** OK; deeper entity-instance lowering is Interpretation’s problem |
| **G7** | No V3 **domain → program / contract interface** lowering | Medium (post-slice) | Interpretation/Domain boundary; pull when codegen/simulation needed |
| **G8** | No V3 **query / overview** helpers | High for MCP | Concise projections for the vertical slice |
| **G9** | **Export/import** portable domain | **Deferred** | Prefer future **DSL spec** as transfer form — not V2 DTO parity in M2 |
| **G10** | **Actor / claims / UAC** | Low for M2 | Genuine expressiveness gap; pull when consumer needs |
| **G11** | Rich **Rule** subtypes vs `DomainExpression` only | Low for M2 | Prefer DE; add rules only if composition fails agents |
| **G12** | Effect **output wiring** (`BindOutputTo`) | Low | Complex chaining only |
| **G13** | **Recipes** (scaffold, OpenAPI/CLR import) | Low | Builders + evolve cover hand path; recipes later |
| **G14** | **Visual** metadata / layout | Deferred | Real-time UI later |
| **G15** | **Demos/benchmarks** still V2 | Medium (WP7 active) | V3 Library + ECommerce demos created; 8 V2 demo files deleted from Benchmarks |
| **G16** | WS7 audit **stale** on lowering | Doc | Lowering exists; update when touching WS7 |
| **G17** | Effect bodies not lowered to executable **programs** | Out of M2 unless runtime sim demanded | Expression/policy eval first; full action simulation later |

### 2.3 What “direct domain API” means (decided direction)

Not a second evolution engine. A **model-optimized library API** that MCP and tests consume:

| Capability | Home | Responsibility |
|------------|------|----------------|
| Bootstrap domain | `DomainModeling` (e.g. factory / builtins) | Empty domain + built-in primitives |
| Evolve | `DomainEvolution` / `EvolutionBuilder` only | Single analysis-gated path on immutable roots |
| Query / views | `DomainModeling` projections | Model-shaped overviews/details — **not** MCP DTOs |
| Workspace / session | **`Poly.Mcp` only** | sessionId, revision, last analysis, current `Domain` root |
| Evaluate | DomainModeling + Interpretation | Policy/expression; tests with C# records; instance models later via Interpretation |
| Serialize | Deferred | Future DSL spec |

**Placement rule:** Domain correctness types live in `DomainModeling`. MCP types may be used by tests that intentionally cover the agent host. Core never references MCP.

**Vertical slice acceptance (M2 minimum):** one chosen entity concept is fully authorable and inspectable via V3 core API + MCP workspace + tests (properties, stages, actions, analysis rollback, overview/detail). Optional second entity only if the slice needs relationships.

---

## 3. Work packages (execution order)

Each package: **goal**, **depends on**, **deliverables**, **acceptance**, **out of scope**, **seed tasks**.

Do packages **in order** unless noted parallel-safe.

---

### WP0 — Plan hygiene & inventory (docs) ✅ in progress via this doc

| | |
|--|--|
| **Goal** | One authoritative gap list and ordered packages |
| **Deliverables** | This file; master roadmap links; task README |
| **Acceptance** | Agents can pick WP1 without re-auditing the tree |
| **Out of scope** | Code |

---

### WP1 — V3 bootstrap + sever V2 from product path

| | |
|--|--|
| **Goal** | Create domains with built-in types without `Poly.Data.Modeling` |
| **Depends on** | — |
| **Deliverables** | |
| | 1. `CanonicalBuiltInTypeCatalog` (or equivalent) under `DomainModeling` that produces `DomainChange`s / evolves a blank `Domain` with string, int, long, bool, decimal, date/time, guid, etc. matching what MCP/bootstrap needs |
| | 2. Fix `PolicyEvaluator` to use **only** `Poly.DomainModeling` (+ Interpretation/Syntax) — remove `using Poly.Data.Modeling` |
| | 3. Grep gate: no `Poly.Data.Modeling` under `Poly/DomainModeling/**` |
| **Acceptance** | Unit test: `DomainFactory.Create("Demo")` (name TBD) yields domain with builtins and clean analysis; PolicyEvaluator builds against V3 `Policy` |
| **Out of scope** | MCP, Actor, contract gen |
| **Seed tasks** | `simple-agent-tasks/wp1-v3-builtin-catalog.md`, `wp1-sever-policyevaluator-v2.md` |

---

### WP2 — Model-optimized direct API (evolve + query)

| | |
|--|--|
| **Goal** | Great library API into the domain model that MCP consumes and tests prefer |
| **Depends on** | WP1 |
| **Deliverables** | |
| | 1. Document **single evolve path**: `DomainEvolution` / `EvolutionBuilder` / `Apply` (no parallel mutators, no core workspace) |
| | 2. **Query projections** in DomainModeling: model-optimized overview/entity/analysis views |
| | 3. Sugar only if the vertical slice proves repetition (discovered, not a second façade) |
| | 4. README: evolve + query without MCP; workspace/session lives in MCP |
| **Design constraints** | |
| | - Immutable roots; efficiency = batch + one analysis gate |
| | - No workspace/session types in DomainModeling |
| | - Queries pure over `Domain` (+ optional `AnalysisResult`) |
| **Acceptance** | TUnit: multi-step evolve success; evolve with bad name → rollback + errors; overview lists entity after add |
| **Out of scope** | Full V2 query surface, Mermaid, visual |
| **Seed tasks** | `wp2-domain-query-projections.md`, `wp2-direct-api-happy-path-tests.md` |

---

### WP3 — Correctness net (tests that define “done”)

| | |
|--|--|
| **Goal** | Behavioral tests that gate M2 |
| **Depends on** | WP1–WP2 (can start stubs earlier) |
| **Deliverables** | Test matrix below green |
| **Acceptance** | All rows pass under TUnit |

| Test scenario | Assert |
|---------------|--------|
| Bootstrap domain | Builtins present; analysis no errors |
| Add entity → property → stage → action | Success; overview counts |
| Invalid evolve (e.g. missing parent entity) | `WasRolledBack`; original domain identity; diagnostics non-empty |
| Policy age guard on VM | true/false for sample records (`ws8-e2e-policy-vm-eval`) |
| Expression lower smoke | Existing + extend DE nodes used by M2 |
| Session-less pure evolve chain | Two successful Applies produce new roots |

**Out of scope:** Full analyzer matrix, V2 parity tests port.

**Seed tasks:** `wp3-e2e-policy-vm-eval.md` (alias of ws8), `wp3-evolution-rollback-suite.md`

---

### WP4 — MCP rewrite (thin, curated)

| | |
|--|--|
| **Goal** | M2 consumer: agents use V3 only via curated tools for the **vertical slice** (1–2 entities) |
| **Depends on** | WP2–WP3 |
| **Deliverables** | |
| | 1. Session store in `Poly.Mcp` holding V3 `Domain` + revision + analysis |
| | 2. Tool inventory sized for the slice (session, overview, get entity, evolve set, analysis) — ≤ ~25; no export/import |
| | 3. **Sharp cliff:** unregister/remove V2 tools from product path when V3 works — no dual stack |
| | 4. Response envelope: success, message, sessionId, revision, diagnostics, affordances |
| | 5. Descriptions written as agent UX |
| | 6. Smoke tests for the vertical-slice multi-tool path |
| **Acceptance** | Chosen entity concept(s) fully workable via MCP + V3 only; no `Poly.Data.Modeling` on that path |
| **Out of scope** | Port all 80 tools; V2 DTO export/import; Actor; full surface flush before slice works |
| **Seed tasks** | `wp4-mcp-session-and-overview.md`, `wp4-mcp-evolve-tools.md`, `wp4-retire-v2-domaintools.md` |

---

### WP5 — Runtime truth polish (pull-only)

| | |
|--|--|
| **Goal** | When tools need eval/lower/codegen, make them reliable |
| **Depends on** | WP3; WP4 if tools expose eval |
| **Deliverables** | |
| | 1. Productized `EvaluatePolicy` / expression eval on direct API |
| | 2. Optional size-limited lowered AST / C# preview for agents |
| | 3. DE lower smoke matrix for nodes MCP policies use (`ws8-domainexpression-lower-smoke-matrix`) |
| **Acceptance** | Documented one-liner + tests for policy eval; no domain opcodes |
| **Out of scope** | Full action/effect simulation engine |
| **Seed tasks** | existing `ws8-*` tasks |

---

### WP6 — V2 freeze ✅

| | |
|--|--|
| **Goal** | Stop investing in V2 |
| **Depends on** | WP4 green (M2) |
| **Deliverables** | Roadmap + AGENTS.md note: no new V2 features; V2 tests deletion-only; inventory of remaining V2 refs |
| **Acceptance** | Written freeze declaration; CI or doc check optional |
| **Status** | ✅ **Done** — freeze declared in AGENTS.md, master-roadmap.md, decision doc |
| **Out of scope** | Deleting code |

---

### WP7 — Port demos / remaining tests off V2 (**aggressive**) ✅

| | |
|--|--|
| **Goal** | Clear the path to delete V2 **quickly** |
| **Depends on** | WP6 |
| **Deliverables** | **Port aggressively** V2 tests that still teach value onto V3; delete redundant V2-only tests rather than preserving them; demos/benchmarks on V3 |
| **Acceptance** | No demo requires V2; V2 test tree shrinking toward empty; no large “oracle” V2 suite retained for comfort |
| **Status** | ✅ **Done** — V3 Library + ECommerce demo domains created; 8 V2 demo files deleted from Poly.Benchmarks; 0 V2 refs in Poly.Benchmarks |
| **Out of scope** | Keeping dual test matrices indefinitely |

---

### WP8 — Delete V2 (**sharp cliff completion**) ✅

| | |
|--|--|
| **Goal** | Single modeling stack |
| **Depends on** | WP7 + WP4 cliff already removed product V2 MCP |
| **Deliverables** | Remove `Poly/Data/Modeling`; fix all references; update placement docs |
| **Acceptance** | Solution builds; V3 + MCP tests green; grep shows no product `Poly.Data.Modeling` |
| **Status** | ✅ **Done** — `Poly/Data/Modeling` (~162 files) deleted; V2 tests (33 files), V2 integration tests, V2 `DomainTools.cs`, V2 MCP tests all deleted. Zero V2 refs in product code. Build 0 errors, 1062 tests pass. |
| **Out of scope** | Soft dual maintenance, “just in case” forks |

---

### WP9 — Pull-only expressiveness (post-M2, consumer-driven)

Ship **only** when MCP/direct API dogfood or next product scenario requires it.

| Feature | Trigger | Notes |
|---------|---------|-------|
| Actor + claims | UAC / principal scenarios in tools | Decision record at kickoff |
| Joint ownership rules | Healthcare multi-own | Analyzer policy change |
| Effect output wiring | Complex action chains in real domain | |
| Contract interface gen | Codegen tool or external consumer | Port patterns from V2 `LowerToContractInterfaces` rules in AGENTS.md |
| Full domain program lowering | Runtime simulation of actions | Separate from expression/policy eval |
| Recipes / OpenAPI import | Interop demos | |
| Visual metadata | Live UI authoring | NodeId-ready |

**Rule:** New `DomainChange` or model type requires a **direct-API call site + test** (and preferably an MCP tool or explicit “next consumer” note).

---

## 4. Milestone mapping

| Milestone | Work packages | Done when |
|-----------|---------------|-----------|
| **M1 Foundation** | — | ✅ Already |
| **M2 First consumer** | WP1–WP4 (+ WP5 if evaluate tool ships) | Direct API + curated MCP on V3 only; test matrix green |
| **M3 V2 freeze** | WP6 | ✅ **Done** — freeze declared 2026-07-10 |
| **M4 V2 delete** | WP7–WP8 | ✅ **Done** — V2 fully removed 2026-07-10 |
| **M5+ Expressiveness** | WP9 | Per consumer pull |

---

## 5. Suggested package layout (implementation sketch)

```
Poly/DomainModeling/
  Bootstrap/          # NEW — built-in catalog, DomainFactory
  Evolution/          # EXISTS — single evolve engine
  Queries/            # NEW — model-optimized projections
  Lowering/           # EXISTS — DE lower + PolicyEvaluator (V3-only)
  Analysis/           # EXISTS
  ...                  # NO Workspace type here

Poly.Mcp/
  Sessions/ or Workspace/   # sessionId, Domain root, revision, analysis
  Tools/                    # curated tools (consumer of DomainModeling)
  Mapping/                  # agent DTOs from domain queries

Poly.Tests/DomainModeling/  # core correctness (no MCP required)
Poly.Tests/Mcp/             # optional — session/workspace via public MCP types
```

**Boundaries:** MCP never owns domain rules; DomainModeling never owns session/workspace.

---

## 6. Direct API surface (minimum for M2 vertical slice)

Illustrative C# — core first; MCP only adds session.

```csharp
// --- DomainModeling (great API / model view) ---
Domain domain = DomainFactory.Create("Orders");

var result = new DomainEvolution(domain).Evolve()
    .AddEntity("Order")
    .AddPropertyToEntity("Order", /* ... */)
    .AddStage("Order", "Draft")
    .AddAction("Order", "Submit")
    .Apply();

if (result.WasRolledBack) { /* diagnostics; domain unchanged */ }
else domain = result.Root;

var overview = DomainQueries.Overview(domain);

// --- Poly.Mcp (workspace) ---
// session holds Domain + Revision + Analysis; tools call DomainEvolution then update session on success
```

---

## 7. MCP tool budget (M2 vertical slice)

From `mcp-guiding-principles.md` — tools needed for **1–2 full entities**, expand only after the slice is solid:

| Group | Tools |
|-------|--------|
| Session | CreateDomainSession, ListSessions (or interrogate) |
| Orient | GetDomainOverview, GetEntity, GetDomainAnalysis |
| Evolve atomic | AddEntity, AddProperty, AddStage, AddAction; relationship only if second entity in slice; minimal removes |
| Evolve composed | optional scaffold for the chosen lifecycle shape |
| Recover | diagnostics on all mutators; affordances |
| Runtime | EvaluatePolicy only if slice includes guard eval with records |
| Portability | **Out** — DSL import/export later |

**Cap:** ~25 for slice. **Sharp cliff:** unregister V2 DomainTools when this ships.

---

## 8. Explicit non-goals (until pulled)

- Full V2 MutationCommand / Intent parity  
- Porting all V2 analyzers 1:1  
- Full effect/action runtime simulator (Interpretation owns instance/runtime model evolution)  
- Actor, visual, recipes, OpenAPI without a consumer  
- Long dual-stack MCP  
- V2-style JSON export/import as the durable format (DSL spec instead)  
- Flushing entire evolve/MCP surface before one vertical entity works  
- Interpretation µop redesign  
- Speculative DomainExpression nodes  

---

## 9. Risks & mitigations

| Risk | Mitigation |
|------|------------|
| “Direct API” becomes a second mutation system | Façade only over EvolutionBuilder; no parallel apply path |
| MCP grows to 80 tools again | Hard budget + principles checklist in PR review |
| PolicyEvaluator / builtins keep V2 alive forever | WP1 grep gate; M4 blocked until clean |
| Under-testing evolve edge cases | WP3 matrix mandatory before MCP rewrite |
| Premature Actor/rules work | WP9 only after M2 dogfood |

---

## 10. Immediate next actions (human / orchestrator)

1. **WP1** — builtins + sever PolicyEvaluator V2. ✅ **Done**
2. **WP2** — query projections + document evolve façade. ✅ **Done**
3. **WP3** — test matrix (include e2e policy VM). ✅ **Done**
4. **WP4** — MCP rewrite against principles. ✅ **Done**
5. **WP5** — runtime truth polish — pull if tools need eval, otherwise skip.
6. **WP6** — V2 freeze declaration.
7. Freeze → port demos → delete V2.

Do **not** open Actor or contract-gen workstreams until M2 is green unless a concrete blocked scenario appears.

---

## 11. Micro-task index

**Rule:** Finish **In Progress** follow-ups before starting Not Started / later WPs. See `simple-agent-tasks/README.md`.

| Task file | Package | Status |
|-----------|---------|--------|
| `simple-agent-tasks/wp1-v3-builtin-catalog.md` | WP1 | **Done** ✅ |
| `simple-agent-tasks/wp1-sever-policyevaluator-v2.md` | WP1 | **Done** ✅ |
| `simple-agent-tasks/wp2-domain-query-projections.md` | WP2 | **Done** ✅ |
| `simple-agent-tasks/wp2-direct-api-happy-path-tests.md` | WP2 | **Done** ✅ |
| `simple-agent-tasks/wp3-evolution-rollback-suite.md` | WP3 | **Done** ✅ |
| `simple-agent-tasks/wp4-mcp-session-and-overview.md` | WP4 | **Done** ✅ |
| `simple-agent-tasks/wp4-mcp-evolve-tools.md` | WP4 | **Done** ✅ (fingerprint no-op guard + tests) |
| `simple-agent-tasks/wp4-retire-v2-domaintools.md` | WP4 | **Done** ✅ |
| `simple-agent-tasks/wp6-declare-v2-freeze.md` | WP6 | **Done** ✅ |
| `simple-agent-tasks/wp7-inventory-v2-tests-and-demos.md` | WP7 | **Done** ✅ / leapfrog delete |
| `simple-agent-tasks/wp7-port-v2-tests-batch1.md` | WP7 | **Superseded** |
| `simple-agent-tasks/wp7-port-v2-demos-batch1.md` | WP7 | **Superseded** |
| `simple-agent-tasks/wp8-delete-v2-gate-check.md` | WP8 | **Done** ✅ — V2 tree deleted |
| `simple-agent-tasks/ws8-e2e-policy-vm-eval.md` | WP5/WS8 | **Done** (domain-attached included) |
| `simple-agent-tasks/ws8-domainexpression-lower-smoke-matrix.md` | WP5/WS8 | **Done** |
| `simple-agent-tasks/ws8-policyevaluator-vm-primary.md` | WP5/WS8 | **Done** |
| `simple-agent-tasks/wp5-optional-mcp-evaluate-policy.md` | WP5/WS8 | **Done** Path B (`get_policy_expression`) |
| `simple-agent-tasks/ws8-spike-policy-sample-subject.md` | WP5/WS8 A+ | **Done** (see #6b–6d follow-ups) |
| `simple-agent-tasks/ws8-spike-harden-negative-subject-tests.md` | WP5/WS8 A+ | **Done** |
| `simple-agent-tasks/ws8-spike-demote-emit-until-proven.md` | WP5/WS8 A+ | **Done** |
| `simple-agent-tasks/ws8-invariant-policy-subject-types.md` | WP5/WS8 A+ | **Not started** ← **next** |
| `simple-agent-tasks/ws8-spike-bool-abi-adult-assert.md` | WP5/WS8 A+ | Not started (bool true ABI) |
| `simple-agent-tasks/ws8-spike-matchnumeric-positive-control.md` | WP5/WS8 A+ | Not started |
| `simple-agent-tasks/ws8-invariant-policy-property-name-alignment.md` | WP5/WS8 A+ | Not started |
| `simple-agent-tasks/ws8-invariant-no-dict-expando-subjects.md` | WP5/WS8 A+ | Not started |
| `simple-agent-tasks/ws8-mcp-add-policy-expression-contract.md` | WP5/WS8 A+ | Not started |
| `simple-agent-tasks/ws8-mcp-add-policy.md` | WP5/WS8 A+ | Not started |
| `simple-agent-tasks/ws8-mcp-evaluate-policy-vm.md` | WP5/WS8 A+ | Not started |
| `simple-agent-tasks/ws8-mcp-policy-e2e-smoke.md` | WP5/WS8 A+ | Not started |
| `simple-agent-tasks/ws8-a-plus-polish.md` | WP5/WS8 A+ | Not started |
| `simple-agent-tasks/ws8-invariant-mcp-tool-honesty.md` | WP5/WS8 A+ | Not started |
| `simple-agent-tasks/ws4-agent-trace-reading-guide.md` | Polish | Not started |

---

## 12. Progress log

| Date | Note |
|------|------|
| 2026-07-10 | Plan created from code audit + consumer/MCP principles. WS7 lowering gap marked stale (pass + VM tests exist). |
| 2026-07-10 | Product decisions: vertical slice; DSL import/export later; records for tests / Interpretation owns instances; sharp V2 cliff; aggressive test port. |
| 2026-07-10 | Direct API: model-optimized DomainModeling API; single evolve path; **workspace/session in MCP only**; sugar discovered while building; tests may use MCP public types for host coverage. |
| 2026-07-10 | Initial WP1–WP4 code landed; review reopened micro-tasks as **In Progress** with follow-ups. **Do In Progress first** before new Not Started work. |
| 2026-07-10 | WP1–WP4 first follow-ups resolved (factory bootstrap, false-positive test, README, silent-no-op doc, MCP structured results/affordances/smoke tests, V2 retirement). |
| 2026-07-10 | **Second review:** reopened `wp4-mcp-evolve-tools` for no-op honesty. |
| 2026-07-10 | **WP4 closed:** fingerprint no-op guard + V3McpSmoke 12/12. Next suite micro-tasks authored (WP6 freeze → WP7 inventory/port → WS8 policy e2e → WP8 gate). |
| 2026-07-10 | Final residual resolved: structural fingerprint guard in `V3EvolveTool.Evolve()` detects zero-effective-change no-ops and returns failure without bumping revision. All WP1–WP4 micro-tasks **Done** ✅ |
| 2026-07-10 | **WP6** V2 freeze declared (AGENTS.md banner, roadmap section, decision doc). **WP7** started: V3 Library + ECommerce + Healthcare demo domains created; V2 demo files deleted from Poly.Benchmarks (8 files). V2 reference grep: Poly.Benchmarks = 0, Poly.Tests = 0 remaining. |
| 2026-07-10 | **WP7/WP8 done** — `Poly/Data/Modeling` (~162 files) deleted. V2 tests (33 files), V2 integration tests, V2 `DomainTools.cs`, and V2 MCP tests all deleted. Zero `Poly.Data.Modeling` references in product code. 1062 tests pass (only pre-existing `VmDebugger_StepOver` fail). Single modeling stack. |
| 2026-07-10 | **Docs sync:** simple-agent-tasks README + master roadmap + plans README retargeted — M1–M4 complete; next = WP5 (`ws8-e2e-policy-vm-eval`). WP7 batch tasks marked Superseded. |

Update this log when a WP completes or a gap classification changes.
