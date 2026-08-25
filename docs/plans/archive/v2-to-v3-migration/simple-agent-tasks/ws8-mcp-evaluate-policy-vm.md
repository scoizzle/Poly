# Micro-Task: MCP `evaluate_policy` — real VM evaluation

**Parent**: WS8 / WP5 (A+ package)  
**Suite:** [`ws8-README.md`](ws8-README.md) **#8**  
**Difficulty**: Medium–Hard  
**Estimated Tokens**: ~10k  
**Status**: [ ] Not Started  
**Depends on**:
- `ws8-spike-policy-sample-subject.md` (Done)
- Prefer `ws8-spike-demote-emit-until-proven.md` + `ws8-invariant-policy-subject-types.md` before or with this task
- Prefer `ws8-mcp-add-policy.md` for full agent loop

## Objective

Honest, working tool: evaluate a named policy on the session domain with sample property values → **`{ result: bool }` via VM**.

Keep `get_policy_expression` as **inspection only**.

## Subject-building rules (from spike + review)

1. **Do not** use `Dictionary<string,object>` or `ExpandoObject` as the VM subject.
2. **Do not** use null nullable value types (`int?` null) — VM unbox fails.
3. **Prefer proven path:** non-nullable CLR properties (`StrictBag`-style) or helper from `ws8-invariant-policy-subject-types.md`.
4. **Reflection.Emit** only if `ws8-spike-demote-emit-until-proven.md` added a **green** Emit test; otherwise do not choose Emit as default.
5. Missing keys → non-null defaults (0, `""`, false), not null.

## Exact Steps

1. Read **revised** `spikes/policy-sample-subject.md` (after #6c if done).
2. Use subject helper if #6d landed; else implement minimal StrictBag-style mapper in DomainModeling (not Dict).
3. Add tool `evaluate_policy`:
   - `sessionId`, `entityName`, `policyName`
   - `properties`: JSON object or structured dict of property name → value
4. Resolve policy from **domain graph** (entity.Policies).
5. Build subject; call `PolicyEvaluator.Evaluate` / `CompileVMPredicate` (VM-primary).
6. Response:
   - Success + `data: { result: true|false, policyName, entityName }`
   - Failures: missing session/entity/policy; bad properties — **never** success without a bool when eval was requested
7. Description must match behavior (VM evaluation, not metadata).
8. Smoke tests:
   - evaluate Age 25 → true, Age 15 → false
   - Missing policy → failure
9. If subject building is blocked, stop and document — do not reintroduce false “eval” claims.

## Invariants

- See `ws8-invariant-mcp-tool-honesty.md`
- See subject rules above

## Verification

- [ ] `get_policy_expression` still inspection-only
- [ ] `evaluate_policy` returns real bool from VM
- [ ] MCP-only smoke for true/false
- [ ] Affordances on failure
- [ ] Tool count reasonable

## Out of Scope

- Full DE node coverage (DateOp/Owned/Rel gaps)
- Contract codegen
- Dual-oracle in product path
