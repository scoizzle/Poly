# MCP guiding principles (for Poly V3 consumer)

**Date:** 2026-07-10  
**Status:** Active — design constraints for M2 MCP rewrite  
**Consumer:** `first-v3-consumer.md`  
**Roadmap:** `master-roadmap.md` Phase 3 / M2  

This note consolidates **external MCP / agent-tool research** with **Poly’s quality bar** (correctness, composition, direct API, tests, natural code). It is the checklist implementers and reviewers use when adding or redesigning tools in `Poly.Mcp`.

---

## Agents and human UI (same quality bar)

MCP is **model-controlled** by protocol design, but a **well-built MCP host surface** can also be excellent for **human UI/UX**:

| Shared asset | Agent value | Human UI value |
|--------------|-------------|----------------|
| Session + revision | Continuity across tool calls | Undo-friendly timeline, optimistic UI |
| Analysis diagnostics | Self-correction | Error panels, inline validation |
| Affordances (next steps) | Planning next tool | Buttons, command palette, “what can I do here?” |
| Concise overview / detail | Context budget | Master–detail screens |
| Atomic + composed evolve | Reliable multi-step | Forms + wizards over the same ops |
| Rollback on failure | Safe agent loops | “Change not applied” without corrupt model |

**Architecture that makes dual use real:**

```
Human UI  ──┐
            ├──→  DomainModeling API (model-optimized, single evolve path)
Agent/MCP ──┘         ▲
                 workspace/session can live in MCP *or* a thin UI host
                 that reuses the same session semantics
```

- **Do** design MCP tools as **capability verbs** on the domain (AddEntity, GetOverview) with stable results — those map cleanly to UI actions.
- **Do not** assume the *only* human path is “drive the LLM that drives MCP.” A first-class UI should call the **same DomainModeling API** (and may share session types or copy MCP session patterns).
- **Do not** specialize the core API for chat transcripts or widget trees — keep it model-shaped; adapters own presentation.

**Implication for M2:** Prefer tool and response shapes that a UI could bind without translation soup (clear success/fail, revision, diagnostics, named entities). That improves agents *and* future human hosts.

---

## Research sources (short)

| Source | Takeaways we adopt |
|--------|--------------------|
| [Anthropic — Writing effective tools for agents](https://www.anthropic.com/engineering/writing-tools-for-agents) | Design for agents, not for REST parity; fewer high-impact tools; namespace; return high-signal context; token-efficient responses; prompt-engineer descriptions; **eval-driven** improvement |
| [Phil Schmid — MCP server best practices](https://www.philschmid.de/mcp-best-practices) | MCP is a **UI for agents**; outcomes over operations; flat args; instructions are context; curate ruthlessly; name for discovery; paginate |
| [AWS Prescriptive Guidance — MCP tool strategy](https://docs.aws.amazon.com/prescriptive-guidance/latest/mcp-strategies/mcp-tool-strategy.html) | Tool count is a product decision: too few → guesswork; too many → confusion; scope granularity deliberately |
| [MCP specification — Tools](https://modelcontextprotocol.io/specification/2025-06-18/server/tools) | Model-controlled tools; human-in-the-loop recommended; validate inputs; tool vs protocol errors; **annotations** for destructive / open-world hints; structured content + schemas |
| [Block — Designing MCP servers](https://engineering.block.xyz/blog/blocks-playbook-for-designing-mcp-servers) | Workflow-first, top-down from agent goals, not bottom-up from every API endpoint |
| Practitioner consensus | Responses re-prompt the model (next steps, errors); session lifecycle explicit; avoid context dumps |

---

## Core tension (resolve deliberately)

| Layer | Design pressure | Poly rule |
|-------|-----------------|-----------|
| **Direct domain API** | Composable, small ops, testable, natural C# | **Composition lives here** — `Evolve` / `Apply` batches, fluent builders, clear queries |
| **MCP tools** | Agent UI: limited context, selection cost, multi-step tax | **Curation lives here** — fewer tools, goal-oriented where it helps, flat schemas, high-signal results |

**Do not** re-create V2’s ~80 tools as a 1:1 mutation mirror.  
**Do not** hide the domain behind a single opaque mega-tool with no recoverable structure.

**Do:** keep a **small set of agent-facing tools** that call a **rich composable direct API**. Batch multi-change evolves inside the server when the agent’s goal is multi-step; expose fine-grained evolve tools only when the agent needs intentional single-step authoring with intermediate feedback.

```
Agent goals
    │ curated MCP tools (few, clear, described)
    ▼
Direct domain API (many composable ops + Apply(list))
    ▼
DomainModeling / analysis / VM
```

This is how we honor both “robustness via composition” and industry “outcomes over endpoint mirrors.”

---

## Principles (normative for M2+)

### 1. Thin MCP, fat direct API

- Tool methods: session resolve → map args → call direct API → map `EvolutionResult` / query DTOs → response envelope.
- No domain analysis rules, NodeId policy, or mutator logic in tool bodies.
- Same happy path runnable from TUnit without MCP.

### 2. Curate the tool surface (not REST parity)

- Target **roughly one focused set** for M2 (order of **~10–25 tools**, not 80+). Expand only when dogfood / eval shows a missing goal.
- Prefer **workflow tools** for common multi-step goals (e.g. scaffold entity + property + stage) implemented as composed `Apply` on the direct API.
- Prefer **atomic evolve tools** when intermediate analysis feedback is the point (agent authoring loop).
- Delete or never port tools that only exist because V2 had a mutator of that name.
- Group related tools with consistent **namespacing** (e.g. `poly_domain_*` or clear `Domain*` prefixes clients can list together).

### 3. Outcomes *and* recoverable steps

- Each tool answers: **when to use it**, **what succeeds**, **what the domain looks like after**.
- Successful mutations return: `success`, short `message`, `sessionId`, `revision`, **actionable diagnostics** (if any), and **next affordances** (suggested tools + args) — Poly already has `DomainAffordance`; keep that pattern.
- Failures (analysis reject, missing entity): **domain unchanged** (rollback), diagnostics in natural language, and a suggested recovery path — not opaque exception dumps.

### 4. Descriptions and schemas are agent UX

Tool `Description` and parameter docs are loaded into context every turn. Treat them as product copy:

| Element | Guidance |
|---------|----------|
| Description | Purpose + when to use + when *not* to use + what it returns |
| Parameters | Flat primitives/enums; names like `entityName`, not `entity`; defaults where safe |
| Nested bags | Avoid free-form `dict` / untyped JSON unless a single documented intent DTO with a discriminator |
| Examples | Prefer one concrete example in description for non-obvious args (e.g. node paths) |
| Overlap | If two tools can do similar work, state the preferred tool and the exception case |

Prompt-engineer descriptions; refine after watching real agent transcripts (Anthropic’s eval loop).

### 5. High-signal, token-efficient responses

- Default to **concise** projections (lists of names + counts; detail tools for one entity).
- Optional `detail` / `responseFormat` (`concise` \| `detailed`) when IDs vs names trade off — prefer **names** for agent reasoning; expose technical ids only when needed for the next tool call.
- Paginate or limit large lists (`limit`, `hasMore` / cursor); never dump full lowered AST or full domain graph by default.
- Prefer semantic identifiers (`entityName`, stage name) over opaque handles in default responses.
- Structured results + stable response envelope; include text that restates success/failure for models that underuse structured fields.

### 6. Errors are recoverable context

Two channels (per MCP spec):

1. **Protocol / validation** — bad args, unknown tool → clear schema-level message.
2. **Tool execution** — analysis failure, not found → `success: false` (or `isError`), message the agent can act on, diagnostics list, affordances.

Never return only a stack trace. Prefer: *“Entity 'Order' not found. Use ListEntities or AddEntity first.”*

### 7. Safety and honesty about side effects

- Session-scoped domain mutation is **stateful** — document that tools change the session domain and bump revision.
- Use MCP **tool annotations** where the SDK allows (`readOnlyHint`, `destructiveHint`, `idempotentHint`, `openWorldHint`) so clients can UI-gate destructive ops. Treat annotations as hints, not security.
- Import / replace / remove operations should be clearly named and described as destructive.
- Hosts may add human confirmation; server still validates everything.

### 8. Session and revision discipline

- Explicit create/list session; every mutating tool requires `sessionId`.
- Return `revision` on every response; tools that depend on freshness can mention “use latest revision from last response.”
- Diff / history only if it serves agent recovery or multi-step authoring — not as V2 feature parity.
- Define session lifetime (process-local for stdio M2 is fine; document it).

### 9. Resources and prompts (optional, not M2 blockers)

MCP also has **Resources** (read data without tool call tax) and **Prompts** (named workflows).

| Primitive | Poly use when |
|-----------|----------------|
| **Tools** | Evolve, analyze, evaluate, export (actions + decisions) |
| **Resources** | Optional later: read-only domain overview URI, revision snapshot — reduces tool clutter for pure reads |
| **Prompts** | Optional later: “author lifecycle entity” multi-step recipe |

M2 may stay tools-only; if tool list grows, prefer resources for pure read projections before adding more list tools.

### 10. Eval-driven tool design

- Maintain a small set of **agent tasks** (from the first-consumer happy path + 2–3 harder ones).
- Metrics: task success, tool-call count, token volume, wrong-tool rate, recovery after analysis error.
- Improve **descriptions and consolidation** before adding more DomainChange subtypes.
- Direct-API TUnit remains the correctness net; agent evals measure **ergonomics**.

### 11. Natural names, Poly naming rules

- Tool and parameter names describe **what happens for the domain**, not pattern taxonomy (`AddEntity`, not `ApplyMutationIntent`).
- Align with AGENTS.md naming: identity = concept.
- Avoid V2 stringly `mutationType` switches as the primary surface; if a batch intent tool exists, it is secondary and fully documented.

### 12. Correctness still wins

- Analysis gate and rollback are non-negotiable on every evolve path.
- MCP never “force applies” invalid domains.
- Lower / codegen / VM eval tools are honest about analysis prerequisites.

---

## Anti-patterns (do not)

| Anti-pattern | Why it fails | Poly alternative |
|--------------|--------------|------------------|
| One MCP tool per V2 mutator (~80 tools) | Selection noise, context bloat | Curated set + direct API composition |
| Single mega-mutation with free-form JSON bag | Hallucinated keys, hard to test | Flat tools + typed batch evolve on direct API |
| Dump full domain / full AST every call | Token waste, diluted signal | Overview + detail + optional lower |
| Business rules only in MCP | Untestable, diverges from CLI/tests | Direct API owns rules |
| Cryptic UUIDs as primary identifiers | Agent confusion | Names first; ids optional |
| Silent success with empty body | Agent can’t plan next step | Message + revision + affordances |
| Raw exceptions as tool results | No recovery path | Structured fail + diagnostics + next steps |
| Preserving V2 tool names for “compat” | Zero product consumers of V2 | Redesign freely |

---

## Current prototype gap (`Poly.Mcp` today)

Observation for the rewrite (not a blame list):

- **~80+ tools** in `DomainTools.cs` — classic API/mutator mirror.
- Rich **affordances** and response envelopes already point the right direction — keep and tighten.
- Heavy V2 coupling (`CreateMutation`, string `mutationType`) — replace with direct V3 evolve API.
- Query tools (list/get entity) are useful patterns; collapse redundant list/get pairs only if detail can be progressive.

M2 rewrite should **shrink and re-shape**, not port tool-for-tool.

---

## Suggested M2 tool inventory (starting point)

Not a frozen list — a **budget** to argue against. Group by goal.

| Goal | Tools (illustrative) | Notes |
|------|----------------------|--------|
| Session | `CreateDomainSession`, `ListDomainSessions` (or interrogate) | Process-local ok |
| Orient | `GetDomainOverview`, `GetEntity`, `GetDomainAnalysis` | Concise defaults |
| Evolve (atomic) | `AddEntity`, `AddProperty`, `AddStage`, `AddAction`, `AddRelationship`, `Remove…` (few) | Map 1:1 to direct API |
| Evolve (batch / outcome) | `ApplyDomainChanges` *or* `ScaffoldLifecycleEntity` | Composed Apply underneath |
| Recover | Diagnostics on all mutators; optional `DiffRevisions` | Affordances after fail |
| Runtime truth | `EvaluatePolicy` / expression eval | Only when direct API supports e2e |
| Portability | `ExportDomain`, `ImportDomain` | Destructive import described as such |
| Power (optional M2+) | `GetLoweredAst`, `GenerateCSharp` | High detail, opt-in, size-limited |

**Hard budget for first ship:** stay under ~25 tools unless eval proves need. Prefer server-side composition over new tools.

---

## Implementation checklist (PR review)

- [ ] Tool calls only direct domain API (no `Poly.Data.Modeling` mutators)
- [ ] Description states when / when not / result shape
- [ ] Args flat; enums constrained; no free-form bag without schema
- [ ] Success and failure return revision (or explain none), diagnostics, affordances
- [ ] Default response is concise; large payloads limited or paged
- [ ] Destructive tools named and described as such; annotations if available
- [ ] TUnit covers direct-API path for the same scenario
- [ ] No new DomainChange without a direct-API call site used by a tool or test
- [ ] Names read as domain language, not V2/intent vocabulary

---

## How this relates to the quality bar

| Quality focus | MCP principle |
|---------------|---------------|
| System correctness | Analysis gate, rollback, honest diagnostics (§12, §6) |
| Robustness via composition | Composition on direct API; batch/outcome tools compose server-side (§1–2) |
| MCP + direct API guiding light | Tool inventory only for agent goals on that path (§2, inventory) |
| Tests | Direct API TUnit + agent-task evals (§10) |
| Natural-reading code | Descriptions, names, flat schemas (§4, §11) |

---

## References (bookmark)

1. Anthropic Engineering — *Writing effective tools for agents* (2025)  
2. Phil Schmid — *MCP is Not the Problem, It's your Server* (2026)  
3. AWS Prescriptive Guidance — *MCP tool design strategy*  
4. Model Context Protocol — *Tools* specification (2025-06-18+)  
5. Block Engineering — *Playbook for designing MCP servers* (2025)  
6. Poly — `first-v3-consumer.md`, `master-roadmap.md`, core engineering principles  

Update this document when dogfood or evals force a principle change; do not silently grow the tool list past the budget without an explicit note here or on the roadmap.
