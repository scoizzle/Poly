# Micro-Task: Q3′ residual — Guide honesty (quantifiers + eval claim)

**Suite:** [`qe-README.md`](qe-README.md) **#Q3.R1**  
**Parent:** [`../dsl-query-surface.md`](../dsl-query-surface.md)  
**Difficulty:** Small Model Friendly  
**Estimated Context:** ~5k tokens  
**Status:** `[ ]` Not Started  
**Prereq:** Q3.R0 preferred  

## Objective

Product guide must be **internally consistent** about Q3′:

1. Remove `any`/`all`/`none`/`count` from **Not yet shipped** (they are shipped).  
2. Fix the **authoring-complete / evaluate not connected** bullet so it does **not** contradict Q3′ store-aware `EvaluatePolicy` (instance + links).  
3. JSON line: still no quantifiers in `add_policy` JSON.

## Required Reading

- `Poly.Mcp/Docs/poly-dsl-agent-guide.md` — § Collection Quantifiers + Rules + Not yet shipped + Expression Gaps  
- `DomainEntityInstance.EvaluatePolicy` — quantifiers preprocess against store  
- AGENTS.md: guide honesty same PR; rebuild embed after guide edit

## Exact Steps

1. Read current guide blocks (Collection Quantifiers, Rules authoring-complete bullet, Not yet shipped, Gaps table).
2. Delete or rewrite “Not yet shipped” quantifier line (do not list shipped items as unshipped).
3. Rewrite related-eval honesty:
   - **To-one path-prefix / `Rel where` / `Rel exists`:** state what is true (authoring green; eval may still be partial if that remains true — verify before claiming).  
   - **Q3′ quantifiers:** evaluate via `EvaluatePolicy` / store links (true when instances linked).  
4. Ensure JSON policy line mentions no `any`/`all`/`none`/`count` shapes.
5. Rebuild MCP project so `get_dsl_guide` embed matches.

## Verification

- [ ] No “Q3′ not shipped” claim remains
- [ ] Authoring vs eval claims match code paths
- [ ] `dotnet build` Poly.Mcp (or solution) green

## Output

- Updated guide  
- Summary: `../agent-summaries/qe-q3-r1-summary.md`

## Out of Scope

- New quantifier features
- JSON implement

## Status tracking

**Claimed by:**  
**Started:**  
**Notes / Blockers:**
