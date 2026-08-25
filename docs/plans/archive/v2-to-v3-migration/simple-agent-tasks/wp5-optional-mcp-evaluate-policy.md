# Micro-Task: MCP EvaluatePolicy tool

**Parent**: WP5 / WS8  
**Suite:** [`ws8-README.md`](ws8-README.md) **#4**  
**Difficulty**: Medium  
**Estimated Tokens**: ~8k  
**Status**: [x] **Done** — **Honesty fix:** renamed to `get_policy_expression`. Name/description match behavior (inspection only, no false VM eval claim). Returns expression text + metadata. Smoke tests for found/not-found scenarios. Full VM evaluation deferred to WP5.

## Objective

Thin MCP tool: evaluate a named entity policy with sample property values → structured **true/false via VM**.

## What landed (incomplete / incorrect for product)

- `V3EvalTool.EvaluatePolicy` registered in `Program.cs`
- Looks up session → entity → policy by name
- Returns `Success: true` with message that policy was **found** and expression ToString
- **Does not** call `PolicyEvaluator` or the VM
- **No** property-value / args parameters
- Tool **Description** claims: “Evaluates … with the given property values, returning true/false via the VM” — **false**

## Code review findings

| Severity | Finding |
|----------|---------|
| **Critical** | Name + description claim evaluation; behavior is **metadata lookup**. Same honesty class as silent evolve success. |
| **Critical** | `Success: true` without a boolean result misleads agents. |
| **High** | No smoke test in `V3McpSmokeTests` (would lock wrong behavior if added now). |

## Follow-ups (close before Done) — pick **one** path

### Path A — Implement real eval (preferred if tool stays `evaluate_policy`)

1. Add args (e.g. JSON object property→value, or flat limited params).
2. Build subject (CLR type or documented bag strategy) matching policy properties.
3. Call `PolicyEvaluator.Evaluate` / `CompileVMPredicate` (VM-primary).
4. Response: `data: { result: bool, policyName, entityName }`; failures recoverable.
5. Smoke test: session → add entity/property/policy → evaluate → assert bool.

### Path B — Honest metadata tool (if full eval deferred)

1. Rename tool (e.g. `get_policy` / `describe_policy`) **or** rewrite Description to: returns policy metadata only; does **not** execute the guard.
2. Do not claim VM, property values, or true/false evaluation.
3. Optionally keep name only if description is precise and success means “found.”
4. Smoke test for lookup success/failure.

**Do not leave Path A description with Path B behavior.**

## Verification

- [ ] Description/name match behavior
- [ ] Either real VM bool result **or** no eval claims
- [ ] Smoke test for chosen behavior
- [ ] Tool count still reasonable

## Out of Scope

- Full Dictionary entity platform
- Codegen
