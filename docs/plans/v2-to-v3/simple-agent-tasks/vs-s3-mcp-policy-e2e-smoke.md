# Micro-Task: MCP e2e smoke — structure + policy + eval

**Suite:** [`vs-README.md`](vs-README.md) **#3.4**  
**Depends on:** #3.2, #3.3  
**Parent:** Slice 3  
**Difficulty:** Small Model Friendly  
**Estimated Context:** ~4k tokens  
**Status:** [x] Done — 5 MCP smoke tests covering add/evaluate  

## Objective

One MCP-only smoke test: create session → structure (canonical entity) → add_policy → evaluate true → evaluate false (or second subject).

## Required Reading

- `Poly.Tests/Mcp/V3McpSmokeTests.cs`
- Tools from #3.2 / #3.3

## Exact Steps

1. Add test method in MCP smoke suite (or new file under `Poly.Tests/Mcp/`).
2. Use only public MCP tool entry points (same as agents).
3. Assert bool results, not only Success flags.
4. Keep test short and deterministic.

## Verification

- [ ] Smoke green
- [ ] Covers add + eval true/false
- [ ] Build green

## Output

- Test only (unless tiny tool bugfix)
- Summary: “Slice 3 functional path green”

## Out of Scope

- Polish/README (#3.5)
- Performance

## Status tracking

**Claimed by:**  
**Started:**  
**Notes / Blockers:**
