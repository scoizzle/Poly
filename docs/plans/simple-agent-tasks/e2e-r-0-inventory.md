# e2e-r-0 — Inventory paramEnv consumers

**Difficulty:** S  
**Status:** `[ ]`  

## Objective

List every `paramEnv` / `ParameterAccess` check that ignores `PropertyAccess`. No product fix.

## Exact steps

1. Write `docs/plans/simple-agent-tasks/e2e-r-inventory-notes.md`.
2. Table: file, method, consults `ParameterAccess` only? (Y/N), used on product path?
3. Must include: `Eval`, `ValidateAssign`, `BuildPostconditionConstraints`, `ConstraintPropagationAnalyzer`, invoke-arg inference, `EvaluateParameterBindings`, AddDays lowering, `ExpressionTypeAnalyzer`.
4. Note `InvokeAction` arg injection (`DomainEntityInstance.cs` ~386–404).

## File ownership

| Edit | Do not edit |
|------|-------------|
| `e2e-r-inventory-notes.md` | `Poly/**` |

## Status

**Status:** Not Started  
**Claimed by:**  
