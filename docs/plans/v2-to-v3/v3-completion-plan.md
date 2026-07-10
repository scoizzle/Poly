# V3 Completion Plan — Gaps, Work Packages, Execution Order

**Status:** Active  
**Last Updated:** 2026-07-10  
**Purpose:** Single implementation plan for finishing V3 as the only domain modeling stack — from current code to M2 (MCP + direct API), then freeze/delete V2, then pull-only expressiveness.  
**Authority:** Complements `master-roadmap.md` (milestones). This doc owns **what is missing**, **ordered work packages**, **acceptance criteria**, and **micro-task seeds**.  
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
| **G1** | No **direct domain API** façade beyond raw `EvolutionBuilder` / `Domain` | **Blocker** | MCP and tests need stable session + evolve + query surface with natural names and projections |
| **G2** | No V3 **bootstrap / built-in type catalog** | **Blocker** | V2 has `CanonicalBuiltInTypeCatalog`; MCP CreateSession depends on it. V3 must not import V2 for bootstrap |
| **G3** | **V2 leakage into V3** (`PolicyEvaluator` `using Poly.Data.Modeling`) | **Blocker** | V3 must compile/run with zero V2 dependency for product path |
| **G4** | Thin **test net** (6 V3 test files vs ~29 V2) | **Blocker** for ship confidence | Happy path + rollback + policy VM + query projections |
| **G5** | **MCP still V2-shaped** (~80 tools, mutators, intents) | **Blocker** for M2 | Rewrite per MCP principles; not port tool-for-tool |
| **G6** | **Policy / DomainExpression e2e** not productized | High | Expression VM tests exist; full “attach policy on entity → Evaluate with CLR/record” path needs packaging + tests (`ws8-e2e-policy-vm-eval`) |
| **G7** | No V3 **domain → program / contract interface** lowering | Medium (M2 optional) | V2 `DomainImplementationLoweringPass` / `LowerToContractInterfaces` only. Pull when MCP/codegen needs it |
| **G8** | No V3 **query / overview / diff** helpers | High for MCP | Today only inside V2 `DomainDiffUtil` / MCP DTOs |
| **G9** | **Export/import** portable domain | Medium | MCP needs redesigned DTOs over V3 `Domain` |
| **G10** | **Actor / claims / UAC** | Low for M2 | Genuine expressiveness gap; pull when consumer needs |
| **G11** | Rich **Rule** subtypes vs `DomainExpression` only | Low for M2 | Prefer DE; add rules only if composition fails agents |
| **G12** | Effect **output wiring** (`BindOutputTo`) | Low | Complex chaining only |
| **G13** | **Recipes** (scaffold, OpenAPI/CLR import) | Low | Builders + evolve cover hand path; recipes later |
| **G14** | **Visual** metadata / layout | Deferred | Real-time UI later |
| **G15** | **Demos/benchmarks** still V2 | Medium post-M2 | Port after M2 or in parallel with freeze |
| **G16** | WS7 audit **stale** on lowering | Doc | Lowering exists; update when touching WS7 |
| **G17** | Effect bodies not lowered to executable **programs** | Out of M2 unless runtime sim demanded | Expression/policy eval first; full action simulation later |

### 2.3 What “direct domain API” means (design target)

Not a second evolution engine. A **host-facing façade** over what already exists:

| Capability | Suggested home | Responsibility |
|------------|----------------|----------------|
| Bootstrap domain | e.g. `DomainModeling/Bootstrap/` or `DomainFactory` | Empty domain + built-in primitives (string, int, …) |
| Evolve | wrap `DomainEvolution` / `EvolutionBuilder` | Same analysis gate; natural method names already largely present |
| Query | e.g. `DomainQueries` / projection records | Overview, entity detail, analysis summary — **no MCP types** |
| Session (optional core vs MCP) | Prefer **MCP-owned** session store; core stays pure | MCP holds `Domain` + revision + last `AnalysisResult` |
| Evaluate | `PolicyEvaluator` + thin wrappers | Policy/guard → VM bool |
| Serialize (M2+) | Export/import DTOs in DomainModeling or adjacent | Round-trip enough for MCP transfer |

**Placement rule:** types the **test suite** needs without referencing `Poly.Mcp` live in `Poly/DomainModeling/` (or a small `Poly/DomainModeling/Api/` if clarity needs a folder). MCP maps DTOs/tools only.

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

### WP2 — Direct domain API (evolve + query)

| | |
|--|--|
| **Goal** | Stable, naturally named façade that MCP and tests share |
| **Depends on** | WP1 |
| **Deliverables** | |
| | 1. **Domain host API** (names illustrative): create/bootstrap, apply evolve builder or change list, return `EvolutionResult` with diagnostics |
| | 2. **Query projections** (records in DomainModeling): `DomainOverview`, `EntitySummary`, `EntityDetail`, `AnalysisSummary` — name-first, concise |
| | 3. Optional **batch helpers**: e.g. scaffold entity+property+stage as composed changes (server-side composition) |
| | 4. README section in `DomainModeling/README.md`: how to evolve and query without MCP |
| **Design constraints** | |
| | - Prefer extending/documenting `DomainEvolution`/`EvolutionBuilder` over inventing parallel mutators |
| | - Queries pure functions over `Domain` + optional `AnalysisResult` |
| | - No JSON attributes required in core; MCP can map |
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
| **Goal** | M2 consumer: agents use V3 only via curated tools |
| **Depends on** | WP2–WP3 |
| **Deliverables** | |
| | 1. Session store in `Poly.Mcp` holding V3 `Domain` + revision + analysis |
| | 2. Tool inventory ≤ ~25 tools per `mcp-guiding-principles.md` (session, overview, get entity, atomic evolve set, optional batch scaffold, analysis, optional export/import, optional evaluate) |
| | 3. Every tool → direct API only; **delete or stop using** V2 mutators in product path |
| | 4. Response envelope: success, message, sessionId, revision, diagnostics, affordances |
| | 5. Descriptions written as agent UX |
| | 6. Smoke tests or scripted multi-tool scenario (if host allows); else integration tests calling tool methods directly |
| **Acceptance** | Happy path from `first-v3-consumer.md` works end-to-end without `Poly.Data.Modeling` |
| **Out of scope** | Port all 80 tools; Actor tools; full V2 policy rules |
| **Seed tasks** | `wp4-mcp-session-and-overview.md`, `wp4-mcp-evolve-tools.md`, `wp4-mcp-rewrite-retire-v2-tools.md` |

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

### WP6 — V2 freeze

| | |
|--|--|
| **Goal** | Stop investing in V2 |
| **Depends on** | WP4 green (M2) |
| **Deliverables** | Roadmap + AGENTS.md note: no new V2 features; V2 tests deletion-only; inventory of remaining V2 refs |
| **Acceptance** | Written freeze declaration; CI or doc check optional |
| **Out of scope** | Deleting code |

---

### WP7 — Port demos / remaining tests off V2

| | |
|--|--|
| **Goal** | Clear the path to delete V2 |
| **Depends on** | WP6 |
| **Deliverables** | Benchmarks demos on V3 evolve/builders; rewrite or drop V2-only tests that still teach value; integration tests use V3 lowering when needed |
| **Acceptance** | No demo entrypoint requires V2 |
| **Out of scope** | Perfect parity of every V2 test |

---

### WP8 — Delete V2

| | |
|--|--|
| **Goal** | Single modeling stack |
| **Depends on** | WP7 |
| **Deliverables** | Remove `Poly/Data/Modeling` (or quarantine); fix all references; update placement docs |
| **Acceptance** | Solution builds; V3 + MCP tests green; grep shows no product `Poly.Data.Modeling` |
| **Out of scope** | Keeping dual stack “just in case” |

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
| **M3 V2 freeze** | WP6 | Freeze declared |
| **M4 V2 delete** | WP7–WP8 | V2 gone |
| **M5+ Expressiveness** | WP9 | Per consumer pull |

---

## 5. Suggested package layout (implementation sketch)

```
Poly/DomainModeling/
  Bootstrap/          # NEW — built-in catalog, DomainFactory
  Evolution/          # EXISTS — keep as core evolve engine
  Queries/            # NEW — overview/detail projections
  Lowering/           # EXISTS — DE lower + PolicyEvaluator (V3-only)
  Api/                # OPTIONAL folder — façade types if EvolutionBuilder alone is awkward
  Analysis/           # EXISTS
  ...

Poly.Mcp/
  Sessions/           # session store
  Tools/              # curated tool classes (not one 3k-line DomainTools)
  Mapping/            # DTO mapping from DomainModeling queries

Poly.Tests/DomainModeling/
  Bootstrap/
  Api/ or Direct/
  Evolution/
  Lowering/
  Queries/
```

Exact folder names can adjust; **boundaries** matter: MCP never owns domain rules.

---

## 6. Direct API surface (minimum for M2 happy path)

Illustrative C# shape — implement with natural names; adjust to fit existing types.

```csharp
// Bootstrap
Domain domain = DomainFactory.Create("Orders"); // builtins included

// Evolve
var evo = new DomainEvolution(domain);
var result = evo.Evolve()
    .AddEntity("Order")
    .AddPropertyToEntity("Order", new Property("Total", /* int type ref */))
    .AddStage("Order", "Draft")
    .AddAction("Order", "Submit")
    .Apply();

if (result.WasRolledBack) { /* diagnostics */ }
domain = result.Domain;

// Query
var overview = DomainQueries.Overview(domain);
var entity = DomainQueries.GetEntity(domain, "Order");

// Evaluate (when ready)
bool ok = policy.EvaluateOnVm(sampleRecord);
```

MCP tools wrap the same sequence with `sessionId` and envelopes.

---

## 7. MCP tool budget (M2 default)

From `mcp-guiding-principles.md` — **start here**, expand only with evidence:

| Group | Tools |
|-------|--------|
| Session | CreateDomainSession, ListSessions (or interrogate) |
| Orient | GetDomainOverview, GetEntity, GetDomainAnalysis |
| Evolve atomic | AddEntity, AddProperty, AddStage, AddAction, AddRelationship, RemoveEntity (minimal remove set) |
| Evolve composed | optional ScaffoldLifecycleEntity **or** ApplyChangeBatch (typed, not free-form bag) |
| Recover | diagnostics on all mutators; affordances |
| Runtime | EvaluatePolicy (if WP5) |
| Portability | ExportDomain, ImportDomain (after WP2 serialize) |

**Cap:** ~25. Retire V2 DomainTools wholesale when this ships.

---

## 8. Explicit non-goals (until pulled)

- Full V2 MutationCommand / Intent parity  
- Porting all V2 analyzers 1:1  
- Full effect/action runtime simulator  
- Actor, visual, recipes, OpenAPI without a consumer  
- Long dual-stack MCP  
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

1. **Start WP1** — builtins + sever PolicyEvaluator V2.  
2. **WP2** — query projections + document evolve façade.  
3. **WP3** — test matrix (include e2e policy VM).  
4. **WP4** — MCP rewrite against principles.  
5. Freeze → port demos → delete V2.  

Do **not** open Actor or contract-gen workstreams until M2 is green unless a concrete blocked scenario appears.

---

## 11. Micro-task index

| Task file | Package | Status |
|-----------|---------|--------|
| `simple-agent-tasks/wp1-v3-builtin-catalog.md` | WP1 | Not started |
| `simple-agent-tasks/wp1-sever-policyevaluator-v2.md` | WP1 | Not started |
| `simple-agent-tasks/wp2-domain-query-projections.md` | WP2 | Not started |
| `simple-agent-tasks/wp2-direct-api-happy-path-tests.md` | WP2 | Not started |
| `simple-agent-tasks/wp3-evolution-rollback-suite.md` | WP3 | Not started |
| `simple-agent-tasks/ws8-e2e-policy-vm-eval.md` | WP3/WP5 | Not started (exists) |
| `simple-agent-tasks/wp4-mcp-session-and-overview.md` | WP4 | Not started |
| `simple-agent-tasks/wp4-mcp-evolve-tools.md` | WP4 | Not started |
| `simple-agent-tasks/wp4-retire-v2-domaintools.md` | WP4 | Not started |
| `simple-agent-tasks/ws8-domainexpression-lower-smoke-matrix.md` | WP5 | Not started (exists) |
| `simple-agent-tasks/ws4-agent-trace-reading-guide.md` | Polish | Not started (exists) |

---

## 12. Progress log

| Date | Note |
|------|------|
| 2026-07-10 | Plan created from code audit + consumer/MCP principles. WS7 lowering gap marked stale (pass + VM tests exist). |

Update this log when a WP completes or a gap classification changes.
