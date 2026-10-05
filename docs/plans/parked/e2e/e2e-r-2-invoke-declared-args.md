# e2e-r-2 — InvokeAction declared-parameter boundary

**Difficulty:** M  
**Status:** `[ ]`  
**Prereq:** r-1  
**Fleet:** P1-1  

## Objective

`InvokeAction` rejects unknown keys and missing declared params. Unknown keys must not clobber properties or bypass guards.

## Exact steps

1. Failing tests on `DomainEntityInstance`:
   - `InvokeAction_UnknownArgKey_FailsClosed`
   - `InvokeAction_MissingDeclaredParam_FailsClosed`
   - `InvokeAction_ArgNamedLikeProperty_DoesNotBypassGuard` — stored Age 15, gate `Age>=18`, invoke `{"Age":40}` must **not** succeed.
2. Validate `args` against `action.Parameters` **before** injecting into `_values`. Fail-closed (DMEFF007-mirror).
3. Repro domain: `probes/fleet-eval/10-runtime/library.poly`.

## File ownership

| Edit | Do not edit |
|------|-------------|
| `Poly/DomainModeling/Runtime/DomainEntityInstance.cs` (`InvokeAction` / arg inject) | `NotifyTransition` / store |
| `Poly.Tests/DomainModeling/Runtime/**` | exporter |

## Status

**Status:** Not Started  
**Claimed by:**  
