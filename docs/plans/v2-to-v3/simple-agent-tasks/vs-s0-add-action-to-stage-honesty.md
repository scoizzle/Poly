# Micro-Task: Honest `add_action_to_stage` semantics

**Suite:** [`vs-README.md`](vs-README.md) **#0.2**  
**Parent:** Slice 0  
**Difficulty:** Small Model Friendly  
**Estimated Context:** ~5k tokens  
**Status:** [ ] Not Started  

## Objective

MCP tool description and `AddActionToStageChange` must describe the **same** behavior (no “assigns existing” when creating empty stage-local actions).

## Required Reading

- `Poly.Mcp/Tools/V3DomainTools.cs` — `add_action_to_stage`
- `Poly/DomainModeling/Evolution/DomainChange.cs` — `AddActionToStageChange`
- `Poly.Tests/Mcp/V3McpSmokeTests.cs` (if present)

## Product decision (pick one, implement fully)

| Option | Behavior |
|--------|----------|
| **A — Create** | Stage gets a **new empty** stage-local action. Reword tool + description: does **not** say “assigns existing.” |
| **B — Assign** | Stage references/shares the **existing** entity-level action (same name, not a blank twin). Implement real assign/copy. |

**Default if unsure:** **A — Create** (matches current code; docs/tests must match). Prefer **B** only if demos clearly need shared action bodies.

## Exact Steps

1. Read current `AddActionToStageChange.ApplyTo` and MCP `[Description]`.
2. Choose A or B; implement code + descriptions so they agree.
3. Add/adjust one test asserting stage action vs entity action (empty vs shared effects/params).
4. Skim `V3ECommerceDomain` / demos — fix only if they break under your choice.

## Verification

- [ ] Tool Description matches implementation
- [ ] Test locks behavior
- [ ] Build + relevant tests green

## Output

- `DomainChange.cs`, `V3DomainTools.cs`, tests, maybe demos
- Summary under `../agent-summaries/`

## Out of Scope

- New MCP tools
- Effect execution

## Status tracking

**Claimed by:**  
**Started:**  
**Notes / Blockers:**
