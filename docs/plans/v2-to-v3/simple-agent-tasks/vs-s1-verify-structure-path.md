# Micro-Task: Verify structure authoring path is covered

**Suite:** [`vs-README.md`](vs-README.md) **#1.1**  
**Parent:** Slice 1  
**Difficulty:** Small Model Friendly  
**Estimated Context:** ~4k tokens  
**Status:** [ ] Not Started  

## Objective

Confirm (and only if needed, fill) tests for the structure path: bootstrap → entity → property → stage → action → overview/detail → rollback on bad evolve. **Do not rebuild** what already works.

## Required Reading

- `Poly.Tests/DomainModeling/DomainAuthoringHappyPathTests.cs` (or nearest name)
- `Poly.Tests/DomainModeling/Evolution/EvolutionRollbackTests.cs`
- `Poly.Tests/Mcp/V3McpSmokeTests.cs`

## Exact Steps

1. Map the story to existing tests (checklist in your summary):
   - [ ] DomainFactory / create session with builtins
   - [ ] Add entity + property + stage + action
   - [ ] Query overview / entity detail
   - [ ] Analysis failure rolls back
   - [ ] MCP multi-step smoke for structure
2. If a box is empty, add **one** minimal TUnit test — no new features.
3. Run the relevant tests; note any known honesty gaps already owned by Slice 0 (do not fix stage-action honesty here unless 0.2 is done and trivial).

## Verification

- [ ] Checklist complete in summary
- [ ] Gaps closed with tests or explicitly “covered by file X”
- [ ] Build + those tests green

## Output

- Optional small test fixes
- Summary with checklist (required)
- Optional: tick Slice 1 notes in `vertical-slice-finish-plan.md` tracking table only if you were told to update plans

## Out of Scope

- Policy eval
- New MCP tools
- Relationships / effects

## Status tracking

**Claimed by:**  
**Started:**  
**Notes / Blockers:**
