# Fix G2 — `get_policy_expression` agent-readable form (optional)

**Suite:** [`dogfood-fix-README.md`](dogfood-fix-README.md)  
**Finding:** DOGFOOD-S1-MUTATION G2 — expression is C# AST `ToString()` with internal IDs  
**Bucket:** W (Workaround: use `describe_expression`)  
**Difficulty:** Medium  
**Status:** `[ ]` **optional** — do after G1 + G3 unless user prioritizes  

## Objective

Return a stable, agent-consumable representation of a policy expression — preferably the **same JSON shape as `add_policy` input** — alongside or instead of raw `Expression.ToString()`.

## Required Reading

1. Mutation G2  
2. `Poly.Mcp/Tools/DomainTools.cs` — `GetPolicyExpression` (~1026–1062)  
3. How `add_policy` parses JSON expressions (`TryParseExpression` / related in OracleTool or DomainTools)  

## Exact Steps

1. Add a serializer DomainExpression → JSON (or reuse existing printer if any) matching add_policy schema as closely as practical.  
2. `GetPolicyExpression` Data includes e.g. `expressionJson` and optionally keep `expression` as human DSL if `DomainDslPrinter` can print expressions — if not, JSON only is fine.  
3. Test: add policy via JSON → get_policy_expression → parseable / round-tripable enough for agents.  
4. Update tool Description string.

## Definition of Done

- [ ] Agents get non-AST-dump expression payload  
- [ ] Test green  
- [ ] Optional task marked `[x]` or skipped with note  

## Out of Scope

- Full DSL pretty-print of all expression forms if JSON is enough  
- Changing evaluate_policy  

## Status tracking

**Claimed by:**  
**Started:**  
**Notes / Blockers:**  
