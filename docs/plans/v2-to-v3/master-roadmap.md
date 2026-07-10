# V2 → V3 Domain Modeling Port — Master Roadmap

**Status**: Active  
**Last Updated**: 2026-07-10  
**Purpose**: Canonical entry point for execution planning.  
**Related**:
- `docs/decisions/2026-v2-to-v3-domain-modeling-port.md` (strategic plan)
- `docs/decisions/2026-core-engineering-principles.md`
- `docs/decisions/2026-05-31-evolution-layer-design.md`
- `docs/decisions/2026-06-08-vm-as-canonical-semantics.md`
- `docs/decisions/2026-06-08-domain-lowering-boundary.md`

---

## Platform Context (July 2026)

**Interpretation is no longer the critical path.** The direct AST→VM-ABI pipeline is production-capable for DomainModeling proofs:

| Capability | Status |
|------------|--------|
| Direct AST→ABI emitter (`DirectVmAbiEmitter`) | ✅ Sole compile path |
| VM as canonical semantics (no tree-walker) | ✅ |
| DomainExpression → Syntax AST lowering | ✅ `DomainExpressionLoweringPass` |
| DomainModeling → Syntax only (no domain opcodes) | ✅ |
| Perf harness (sieve / mandelbrot / nqueens / collatz) | ✅ Competitive with C#; beats JS family |
| Statement-level `VmDebugger` | ✅ |

**Implication:** Primary agent effort returns to **DomainModeling V2→V3** (evolution, analysis/lowering parity for *consumers*, MCP migration). Further Interpretation work is **on-demand** when a DomainModeling proof or MCP path forces a gap.

---

## Core Rules for All Agents

1. **Consult decisions first** (per AGENTS.md).
2. Before claiming work, check this roadmap + the relevant workstream file.
3. On completion, write a summary in `agent-summaries/` (template there). Prefer not to edit this master file unless you are the orchestrator.
4. Prefer small, verifiable increments (“build working code before abstraction”).
5. Domain model is the key artifact — tools serve it, not the reverse.

See **[orchestration-guide.md](orchestration-guide.md)** for roles, claiming, and summary flow.

---

## High-Level Phases

| Phase | Focus | Status (2026-07-10) |
|-------|-------|---------------------|
| **Phase 1** | Evolution layer foundation + proofs + expressiveness audit | **Substantially complete** — polish only (WS4/WS6) |
| **Phase 2** | Analysis unification + **consumer-facing** lowering parity | **In progress** — DE→AST done; contract/test gen + e2e still open |
| **Phase 3** | Consumer migration (MCP + demos + tests) | **Not started** — highest product leverage after Phase 2 gates |
| **Phase 4** | Full expressiveness + remaining roadblocks | **Design ready** — implement when Phase 2/3 demand it |
| **Phase 5** | Cutover & V2 removal | **Not started** |

---

## Phase 1 Status (Ground Truth)

**Code (verified July 2026):**

- `DomainEvolution.Apply` / `Evolve()` — real applicator via `DomainMutationContext` (not a no-op).
- **66** concrete `DomainChange` subtypes + rich fluent `EvolutionBuilder`.
- Analysis gate + rollback (`EvolutionResult.RolledBack` keeps original root).
- NodeId continuity + incremental analysis via `ModifiedNodes`.
- WS5 proofs claimed complete (PersonLifecycle + Library; see `agent-summaries/ws5-*.md`).
- WS7 expressiveness audit complete (refresh June 2026).

**Remaining Phase 1 (hygiene only):**

| Workstream | File | Status | Next |
|------------|------|--------|------|
| **WS4** Trace quality | `workstreams/ws4-trace-and-rollback-ux.md` | In progress | Agent-facing trace docs + rollback diagnostics polish |
| **WS6** Doc hygiene | `workstreams/ws6-documentation-hygiene.md` | In progress | Keep this roadmap + decisions current (this update is WS6) |
| **WS1** Applicator MVP | `workstreams/ws1-evolution-applicator-mvp.md` | **Complete** | Do not reopen unless a proof fails |
| **WS5** Proofs | `workstreams/ws5-proof-on-examples.md` | **Complete** | Only reopen if a regression appears |
| **WS7** Expressiveness | `workstreams/ws7-v3-expressiveness-audit.md` | **Complete** | Living doc — update when adding Phase 4 features |

Superseded / historical workstream files (`ws1-evolution-layer-core.md`, `ws2-nodeid-continuity.md`, `ws3-mvp-operations.md`) remain for archive only.

---

## Phase 2 Workstreams (Primary Focus Now)

| Workstream | File | Status | Deliverable |
|------------|------|--------|-------------|
| **WS8** Analysis + lowering parity | `workstreams/ws8-analysis-unification-and-lowering.md` | **Active** | Domain → DomainExpression → Syntax → **VM** e2e; V3 contract interface gen; V3 test/program gen parity with V2 where first consumers need it |

### Phase 2 done (do not re-do)

- DomainExpression → Syntax AST (`DomainExpressionLoweringPass`) — all expression kinds.
- Shared Syntax/Analysis substrate (CF, side-effect, mutability, etc.).
- VM execution pipeline hardened (args, heap, NoDebug, benchmarks).
- Policy evaluation path that can target the VM (`PolicyEvaluator`).

### Phase 2 open (ordered by first consumer)

1. **E2E proof tests**: Domain policy / `DomainExpression` → lower → `Interpreter.Compile` → execute with known entity args; assert results (not just lower-to-AST unit tests).
2. **V3 contract interface generation** — port / re-express V2 `LowerToContractInterfaces` rules (`I{Stage}{Entity}` naming, inheritance) against V3 `Domain` (see AGENTS.md Placement Rules).
3. **V3 domain→program generation** (tests/demos) — only as needed for MCP/demo migration.
4. **Gap catalog stay-current** — Actor subtype, rule-composed policies if still missing; keep WS7 table honest.

**Explicit non-goals for Phase 2:** more µop catalogs, speculative IR redesigns, dual execution engines.

---

## Phase 3 (Next After Phase 2 Gates)

- Migrate MCP tools to V3 evolution (`Evolve` / `Apply` + traces).
- Optimize tool surface for how models actually call tools (batching, affordances, feedback).
- Migrate demos and high-value tests off V2 mutators.
- Keep V2 alive only as long as dual maintenance is shorter than cutover.

---

## Phase 4 (Expressiveness — Pull, Don’t Push)

Design docs:
- `docs/decisions/2026-06-phase4-dynamic-calculation-and-readonly-navigation.md`
- WS7 audit table for remaining gaps (Actor, rule-composed policies, etc.)

Implement only when a concrete consumer (MCP scenario, roadblock, UI) needs it. Prefer event/subscription over cross-entity mutation (ownership boundaries).

---

## Immediate Starting Point (July 2026)

### Orchestrator / larger agent

1. Claim **WS8** (or WS4 polish if you prefer smaller scope).
2. Read `workstreams/ws8-analysis-unification-and-lowering.md` + domain-lowering boundary decision.
3. Prefer **e2e DomainExpression→VM tests** before new abstractions.
4. Decompose into micro-tasks under `simple-agent-tasks/` (see README there).

### Executor / smaller agent

1. Open `simple-agent-tasks/README.md`.
2. Pick a task marked **Not Started** whose parent workstream is **WS8** or **WS4** (skip WS1/WS2/WS3 tasks marked Done/Superseded).
3. Follow the task template; file a summary in `agent-summaries/`.

### First concrete wins (recommended order)

1. **E2E policy on VM** — one entity + one policy expression → lower → execute → assert (micro-task: `ws8-e2e-policy-vm-eval.md`).
2. **Contract interface surface** — invent or port the first `I{Stage}{Entity}` generation for a tiny domain (micro-task batch under WS8).
3. **WS4** — document “how agents read EvolutionTrace + diagnostics” in a short agent-facing note.

---

## Multi-Agent Mechanics

Unchanged and still required:

- `orchestration-guide.md` — authority on roles
- `agent-summaries/` — executors report here; orchestrators merge into this roadmap
- `simple-agent-tasks/` — small-model-friendly work units

**Ignition note:** `00-bootstrap-and-ignition-plan.md` is historical for *first* ignition. Current “ignition” is **Phase 2 WS8 re-entry** after Interpretation detour, not a greenfield WS1 claim.

---

## Readiness Checklist

| Item | Status |
|------|--------|
| Evolution layer real (Apply + Evolve + 66 changes) | ✅ |
| PersonLifecycle / Library proofs | ✅ (per WS5 summaries) |
| Expressiveness audit (WS7) | ✅ |
| DomainExpression → AST | ✅ |
| VM sole engine + competitive perf | ✅ |
| E2E DomainExpression → VM tests as product gate | ⬜ |
| V3 contract interface generation | ⬜ |
| MCP on V3 evolution | ⬜ |
| V2 removal | ⬜ |

**Bottom line:** Phase 1 foundation is delivered. Do not restart evolution-layer greenfield work. Drive **WS8 e2e + contract gen**, then **Phase 3 MCP**.

---

## Historical Notes

Code-review merge of WS1/WS2/WS3 and the June 2026 VM hardening notes remain valid history but are no longer the active task list. Prefer the tables above over older “Immediate Starting Point” paragraphs that still say “create WS1 micro-tasks.”
