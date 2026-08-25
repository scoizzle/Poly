# Micro-Task: Name the first V3 consumer (orchestrator decision note)

**Parent Workstream**: Phase 3 / master roadmap M2  
**Difficulty**: Small Model Friendly (decision + write-up only)  
**Estimated Tokens**: ~3k  
**Status**: [x] **Done** (2026-07-10) — see `docs/plans/v2-to-v3/spikes/first-v3-consumer.md`

## Objective

Record a one-page decision: what the **first V3-only consumer** is, so Phase 2/3 work is pulled by that path instead of V2 parity.

**Decision:** MCP thin tools + **direct domain API** (not CLI-first, not demo-only). Quality bar: correctness, composition, tests on the direct API, natural-reading code.

## Context You Need

- `docs/plans/v2-to-v3/master-roadmap.md` § Strategic reality + Phase 3
- `docs/decisions/2026-v2-to-v3-domain-modeling-port.md` § Goals (July 2026)
- Core principle: first consumers before guardrails/features without owners

## Exact Steps

1. Pick **one** of:
   - **A.** MCP tool surface rewritten on `Poly.DomainModeling` + evolution
   - **B.** Thin CLI / file-based evolve + analyze loop
   - **C.** Demo/benchmark harness rewritten off V2
   - **D.** Other (describe)
2. Write `docs/plans/v2-to-v3/spikes/first-v3-consumer.md` with:
   - Choice + one-paragraph why
   - Happy path (3–7 steps a user/agent takes)
   - V3 APIs required (Evolve ops, analysis, VM eval?, codegen?)
   - Explicit **out of scope** for M2
   - Suggested freeze/delete order for V2 remnants
3. Do **not** implement the consumer in this task.

## Verification

- [x] File exists and names a single consumer
- [x] Lists only capabilities needed for that path
- [x] States V2 has no product consumers (current reality)

## Output

- `docs/plans/v2-to-v3/spikes/first-v3-consumer.md` ✅
- Agent summary: `agent-summaries/orchestrator-july-2026-mcp-direct-api-quality-bar.md` ✅

## Out of Scope

- Implementing MCP/CLI
- Porting Actor or full expressiveness
- Touching V2 except noting what will be deleted
