# First V3 consumer (decision)

**Date:** 2026-07-10  
**Status:** Decided  
**Roadmap:** M2 in `docs/plans/v2-to-v3/master-roadmap.md`  
**Related decision:** `docs/decisions/2026-v2-to-v3-domain-modeling-port.md`  
**MCP design principles:** `mcp-guiding-principles.md` (agent-tool research + Poly quality bar)

## Choice

**MCP tool surface rewritten on V3, sitting on a direct domain API.**

Not “MCP only,” and not “CLI first then maybe MCP.”

| Layer | Role |
|-------|------|
| **Direct domain API** | **Great, model-optimized API** into the system: immutable `Domain`, single evolve path (`Apply` / `Evolve`), model-shaped queries, lower/eval as needed. Readable C#; tests prefer this. **No workspace type here.** |
| **MCP** | **Consumer** of that API: **workspace/session** (sessionId, revision, analysis), tool descriptions, affordances, agent envelopes. Does not reimplement domain rules. |

**Why:** Agents are the near-term scenario, but the library API is the optimized view of the *model*. MCP adapts; it does not own domain semantics. Workspace thrash ends when session lives only in MCP.

**V2 reality:** Zero product consumers. Free to redesign tools and API shapes. Do not preserve V2 mutation/intent shapes for compatibility.

## Quality bar (non-negotiable for this consumer)

1. **System correctness** — analysis-gated evolution, rollback on error, diagnostics that match reality; lowered expressions evaluate as intended on the VM.
2. **Robustness via composition** — small, composable operations on the **direct API**; multi-step workflows composed via `Apply` / fluent evolve. MCP may expose **curated** outcome tools that call that composition — not an 80-tool mutator mirror, and not one opaque mega-bag.
3. **MCP + direct API as guiding light** — every DomainModeling feature is justified by a call site on the direct API that MCP (or a test of that API) uses. No speculative surface. Follow `mcp-guiding-principles.md` for tool count, descriptions, responses, errors.
4. **Tests help** — direct API covered by TUnit first; MCP smoke / agent-task evals reuse the same scenarios. Prefer behavioral tests over structure-only asserts.
5. **Code that reads naturally** — fluent `Evolve()` / named ops over opaque stringy mutation bags; tool names and descriptions describe *what* happens for the domain, not pattern taxonomy.

## Happy path (agent / human)

1. Create session (or open domain) → empty or bootstrap domain via direct API.
2. Evolve: add entity → property → stage → action (composed steps or one multi-change apply).
3. On analysis failure: see diagnostics, domain unchanged (rollback), fix and retry.
4. Attach a simple policy / guard as `DomainExpression`.
5. Query overview (entities, stages) for the next edit.
6. (When needed) Evaluate policy/guard on VM with sample args; get bool / structured result.
7. (Optional M2+) Lower node / emit C# for inspection — only if the thin MCP path needs it for agents.

## V3 APIs required for M2

| Capability | Required? | Notes |
|------------|-----------|--------|
| `DomainEvolution.Apply` / `Evolve()` + analysis gate + rollback | **Yes** | Core of direct API |
| Enough fluent ops for entity/property/stage/action/basic relationship | **Yes** | Already largely present |
| Session or in-memory domain handle usable from MCP | **Yes** | MCP-owned; stores `Domain` + revision + last analysis |
| Readable `EvolutionResult` / diagnostics for agents | **Yes** | WS4 polish if gaps show up in dogfood |
| DomainExpression → Syntax → VM eval | **When** first policy tool needs runtime truth | `ws8-e2e-policy-vm-eval` |
| Contract interface / full C# program gen | **No** for minimal M2 | Pull later if tools demand |
| Export/import portable payload | **Out of M2** | Future **DSL spec** is the preferred portable form |
| Vertical depth | **In** | **1–2 entity concepts fully working** before breadth flush |

## Explicit out of scope for M2

- Full V2 tool-count parity or V2 intent/mutation type names
- Actor model, full Rule-system port, roadblocks
- Long-lived V3→V2 adapter
- Dual-stack MCP (V2 and V3 side by side as product) — **sharp cliff** off V2 tools
- V2-style JSON domain export/import as durable format
- Speculative analyzers or DomainChange types without a direct-API call site
- Flushing entire surface before one vertical entity works

## Suggested freeze / delete order for V2 remnants

1. **M2 green** — vertical slice on V3 MCP + tests; **unregister V2 MCP tools immediately** (sharp cliff).
2. **M3 freeze** — no new `Poly/Data/Modeling` features.
3. **Aggressive test port** — move valuable V2 tests to V3; delete the rest; demos off V2.
4. **Delete** `Poly/Data/Modeling` when references are gone.

## Architecture sketch

```
Agent / IDE
    │ MCP tools (thin)
    ▼
Direct domain API  ◄── unit/integration tests (primary correctness net)
    │  Evolve / Apply, query projections, lower, eval
    ▼
Poly.DomainModeling  →  Syntax AST  →  Interpretation (VM)
```

Composition: prefer many small direct-API methods and MCP tools that map 1:1 (or nearly) over one mega-mutation tool. Batch apply remains one composed `Apply(changes)` under the hood.

## Status of naming task

`simple-agent-tasks/ws3-name-first-v3-consumer.md` → **Done** by this document.
