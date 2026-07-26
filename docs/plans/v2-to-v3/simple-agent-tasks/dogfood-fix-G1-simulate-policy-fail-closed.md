# Fix G1 — `simulate_policy` fail-closed on unresolved properties

**Suite:** [`dogfood-fix-README.md`](dogfood-fix-README.md)  
**Finding:** DOGFOOD-S1-MUTATION G1 — unknown property in expression vs bag returns **true**  
**Bucket:** R (Runtime surprise)  
**Difficulty:** Small–Medium  
**Status:** `[x]` — product fix + regression test

## Objective

`simulate_policy` must **not** return success `result: true` when the expression references a property that is missing from the subject bag (or cannot resolve). Fail closed: `Success: false` **or** `result: false` with a clear message — pick one and document; prefer **Success: false** with diagnostic text for agent visibility.

## Required Reading

1. Finding G1 in `../agent-summaries/dogfood/DOGFOOD-S1-MUTATION-FINDINGS-20260725.md`  
2. `Poly.Mcp/Tools/OracleTool.cs` — `SimulatePolicy` (~596–638)  
3. How `DomainEntityInstance.EvaluatePolicy` treats missing properties (grep `EvaluatePolicy` / property access)

## Exact Steps

1. Reproduce: expression comparing `NonExistent` (or property not in bag) with a bag that only has other keys — confirm current true/fail-open.  
2. Decide behavior (recommended):
   - If expression references `PropertyAccess` names not present in subject bag → return `Success: false`, Message explains missing properties.  
   - Do **not** invent properties on the synthetic entity solely to make unknown names “exist” as null/Text that compare truthy.  
3. Implement the smallest fix in oracle/runtime path used by `SimulatePolicy` (prefer MCP boundary validation of referenced names vs bag keys if runtime is shared with evaluate_policy — be careful not to break intentional “property missing → false” semantics on **store** evaluate if different; match product fail-closed).  
4. Test in `Poly.Tests` (MCP or DomainModeling):
   - `SimulatePolicy_UnknownProperty_FailsClosed` (or Method_Condition_ExpectedResult naming)  
   - Existing simulate_policy true/false cases still green  

## Definition of Done

- [x] Unknown property no longer yields silent `result: true` success  
- [x] Clear failure message for agents  
- [x] Automated regression test  
- [x] `dotnet build` + targeted tests green  
- [x] This file Status `[x]`; fix-README CURRENT → G3  

## Verification

```bash
dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/*/*SimulatePolicy*'
```

## Out of Scope

- get_policy_expression formatting (G2)  
- StoragePass (G3)  
- JSON policy feature expansion  

## Status tracking

**Claimed by:**  
**Started:**  
**Notes / Blockers:**  
