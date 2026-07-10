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
| **Direct domain API** | Primary product surface: evolve (`Apply` / `Evolve`), analyze, lower DomainExpression → Syntax AST, evaluate on VM when needed, export/import snapshots. Readable C# that a human or test calls without MCP. |
| **MCP** | Thin adapter: sessions, JSON DTOs, tool descriptions, affordances. Calls the direct API; does not reimplement domain rules. |

**Why:** Agents are the near-term product path (`Poly.Mcp` already exists as a V2-shaped prototype). The direct API is what correctness and composition attach to: tests, demos, and any future CLI share one path. MCP is the guiding *scenario*; the direct API is the guiding *contract into the rest of the system*.

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
| Export/import portable payload | **Nice** | Redesign DTOs; not V2 parity |

## Explicit out of scope for M2

- Full V2 tool-count parity or V2 intent/mutation type names
- Actor model, full Rule-system port, roadblocks
- Long-lived V3→V2 adapter
- Dual-stack MCP (V2 and V3 side by side as product)
- Speculative analyzers or DomainChange types without a direct-API call site

## Suggested freeze / delete order for V2 remnants

1. **M2 green** — new MCP + direct API path works for happy path above; tests on direct API.
2. **M3 freeze** — no new `Poly/Data/Modeling` features; document in roadmap + AGENTS.md.
3. **Delete in order:** rewrite/remove `Poly.Mcp/DomainTools.cs` V2 paths → move demos off V2 → delete or quarantine V2 tests → remove `Poly/Data/Modeling`.

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
