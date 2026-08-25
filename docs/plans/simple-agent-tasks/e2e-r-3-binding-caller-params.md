# e2e-r-3 — Cross-entity invoke param-ref bindings

**Difficulty:** M  
**Status:** `[ ]`  
**Prereq:** r-2  
**Fleet:** P1-3  

## Objective

`invoke invoice.Settle(amount: amount)` passes the **caller action param**, not the instance handle.

## Exact steps

1. Failing runtime test + export-agree test. Repro: `probes/fleet-eval/11-vm/vm-crossinvoke.poly`. Name: `InvokeAction_CrossEntity_ParamRef_PassesCallerArg`.
2. Thread action params through `EvaluateParameterBindings` (today: entity-only provider).
3. Same binding path used by `for` / `ApplyForEachInvoke` if it shares `Eval` — do not leave a second provider.

## File ownership

| Edit | Do not edit |
|------|-------------|
| `DomainEntityInstance.cs` (binding / invoke-arg eval) | MCP JSON (r-9) |
| tests | `MinimalApiGenerator` |

## Status

**Status:** Not Started  
**Claimed by:**  
