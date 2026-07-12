# Micro-Task: Document `add_policy` expression contract

**Parent**: WS8 / WP5 (A+ package)  
**Suite:** [`ws8-README.md`](ws8-README.md) **#7a**  
**Difficulty**: Small  
**Estimated Tokens**: ~3k  
**Status**: [ ] Not Started  
**Depends on**: None — do **before or with** `ws8-mcp-add-policy.md`

## Objective

Freeze a **constrained** expression surface for MCP `add_policy` so agents don’t get free-form AST JSON bags (anti-pattern from MCP principles).

## Exact Steps

1. Write `docs/plans/v2-to-v3/spikes/mcp-add-policy-expression-contract.md` with:
   - Supported ops: e.g. `==`, `!=`, `>`, `>=`, `<`, `<=` on one property vs literal
   - Optional single `and` / `or` of two comparisons (if implementing in #7)
   - Literal types: number, string, bool
   - Explicit **unsupported** for M2: nested DE, DateOp, RelationshipNav, free AST
   - Mapping table: JSON fields → `DomainExpression` factory calls
   - Error messages agents should see for unsupported shapes
2. Point `ws8-mcp-add-policy.md` implementers at this contract (link in that task if missing).
3. Do **not** implement the tool here unless trivial — contract only is enough.

## Verification

- [ ] Spike file exists and is implementable without design debate
- [ ] Matches existing DE nodes we already evaluate on VM (comparison/and/or/not)

## Out of Scope

- Tool implementation (#7)
- Full DomainExpression JSON AST
