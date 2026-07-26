# Fix S1-R-B1 — `require not PolicyName` guard negation

**Source findings:** DOGFOOD-S1-RERUN B1  
**Bucket:** R (Runtime surprise)  
**Difficulty:** Small  
**Status:** `[x]`

## What was done

**Root cause:** When an action declares `require not PolicyName`, the parser creates a synthetic action-level guard `not_PolicyName` with `Not(Policy.Expression)`. This correctly evaluates the negation. However, entity-level policies (and stage-level policies) are also evaluated as unconditional guards on every action invocation. So even though `not_AtLimit` correctly passes when `AtLimit` is false, the entity-level `AtLimit` policy itself was ALSO evaluated as a guard and blocked the action.

**Fix:** In `DomainEntityInstance.InvokeAction`, when evaluating entity-level and stage-level policies as guards, skip those whose names are inverted by an action-level `require not` guard (detected by the `not_{PolicyName}` naming convention). This ensures that `require not AtLimit` on the action exempts the action from the entity-level `AtLimit` guard.

**Tests added:**
| Test | What it verifies |
|------|-----------------|
| `InvokeAction_RequireNotPolicy_WhenPolicyFalse_Succeeds` | `require not AtLimit` succeeds when AtLimit is false (Value=0, Max=5). Action executes, value increments. |
| `InvokeAction_RequireNotPolicy_WhenPolicyTrue_Fails` | `require not AtLimit` fails when AtLimit is true (Value=5, Max=5). Clear error message. |

## Files changed

- `Poly/DomainModeling/DomainEntityInstance.cs` — skip entity/stage policies that are inverted by action `require not` guards
- `Poly.Tests/Mcp/McpSmokeTests.cs` — 2 regression tests

## Verification

```bash
dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj
dotnet run --project Poly.Tests/Poly.Tests.csproj
```

Total: 1630 passed (2 new).
