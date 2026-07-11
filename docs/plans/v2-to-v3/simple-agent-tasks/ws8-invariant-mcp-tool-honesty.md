# Micro-Task: Invariant — MCP tool honesty (ongoing)

**Parent**: WS8 / MCP principles  
**Suite:** [`ws8-README.md`](ws8-README.md) **#11**  
**Difficulty**: Small  
**Estimated Tokens**: ~3k  
**Status**: [ ] Not Started  
**When:** After #8 lands, or anytime new policy tools are added

## Objective

Prevent regression of the `evaluate_policy` honesty bug: tool **name + Description + Success** must match actual behavior.

## Invariant

| If the tool… | Then… |
|--------------|--------|
| Name/Description says evaluate / VM / true-false | Must call `PolicyEvaluator` (VM) and return `data.result: bool` |
| Only looks up metadata | Must be named/described as inspect/get/describe — **never** “evaluates via VM” |
| Evaluation requested but fails | `Success: false` (or explicit error), not success without a bool |

## Exact Steps

1. Add a short section to `Poly.Mcp/README.md` under tools: honesty rule + list current policy tools (`get_policy_expression` inspect; `evaluate_policy` eval when present).
2. Optional guardrail test: reflect on `[McpServerTool]` descriptions for policy tools — if name contains `evaluate` and description contains `VM` or `true/false`, assert the method body references `PolicyEvaluator` or `CompileVMPredicate` (string/source check is fragile; prefer a documented checklist + code review note).
3. Prefer: smoke tests that lock behavior (inspect ≠ bool result; evaluate returns bool).
4. When implementing #8, ensure `get_policy_expression` remains inspection-only.

## Verification

- [ ] README states the invariant
- [ ] No tool named/described as eval without VM bool path
- [ ] Smokes cover both tools if both exist

## Out of Scope

- Implementing evaluate itself (#8)
