# e2e-r-1 — paramEnv treats PropertyAccess (L3)

**Difficulty:** M  
**Status:** `[ ]`  
**Prereq:** r-0  
**Fleet:** P2-1  

## Objective

Call-chain / binding analysis sees action parameters authored as bare identifiers.

## Exact steps

1. Failing test from `probes/fleet-eval` chainparam / 05-F1: `invoke Add(amount: 50)` on `Total range(0,100)` produces the `DoIt → Add` call-chain warning. Name: `ConstraintPropagation_CallerLiteral_ToCalleeParam_Warns`.
2. One helper: “this PropertyAccess name is in paramEnv → treat as parameter.” Use it at every inventory row marked Y.
3. Do not change `DslExpressionParser.ParsePrimary`.
4. Do not add a `param` keyword.

## Verification

- [ ] 05-F1 repro green  
- [ ] Suite green  

## File ownership

| Edit | Do not edit |
|------|-------------|
| analysis files listed in inventory (`ExpressionTypeAnalyzer` only if a row requires it — prefer r-4 for Unknown-bypass) | `DslExpressionParser.cs` |
| `Poly.Tests/DomainModeling/Analysis/**` | MCP tools |
| inventory notes (check off rows) | `DomainEntityInstance.InvokeAction` (r-2) |

## Status

**Status:** Not Started  
**Claimed by:**  
