# create-create-in-5 — MCP simulate = bind + Interpreter

**Difficulty:** S  
**Status:** `[ ]`  
**Prereq:** task 4 (or 2 if store-reads still pending and MCP tests do not need Rel exists in the same action)

## Objective

MCP harness allocates a dictionary-backed instance, binds the session Store, and runs the **cached lowered program**. Tool descriptions match simulate-on-lowered-AST.

## Required reading

1. Parent L4 / L5 / success
2. `Poly.Mcp/Tools/RuntimeTool.cs` (`create_instance`, `invoke_action`)
3. CORE §3.6 MCP principle
4. `Poly.Tests/TestHelpers/PolicySubject.cs` — test-only CLR wrapper; do not confuse with VM type-def path

## Exact steps

1. Confirm `create_instance` already builds `DomainEntityInstance` (implements `IDictionary<string, object?>`) and registers it on `DomainInstanceStore`. Do not wrap Expando. Do not add a third instance type.
2. `invoke_action` / `evaluate_policy` must run `Interpreter` on the lowered operation/policy AST with `This` = that instance (Store already bound via `TryAdd`). Cache compile per (entity, action/policy) if it currently re-lowers from Effect IR every call — the cache key is the lowered tree, not Domain walk.
3. Update tool descriptions if they still say “thin wrapper around DomainEntityInstance” in a way that implies a second evaluator. Honesty: bind + run the program.
4. No production change to `PolicySubject` unless a test falsely blocks dictionary-backed VM subjects. That helper is test-only.

## Verification

```bash
dotnet run --project Poly.Tests/Poly.Tests.csproj -p:NuGetAudit=false -- --treenode-filter "/*Mcp*|/*RuntimeTool*|/*InvokeAction*"
dotnet run --project Poly.Tests/Poly.Tests.csproj -p:NuGetAudit=false
```

- [ ] create_instance + invoke_action create-in still registers a linked child in the session store
- [ ] Unique collision is a tool Failure, not a crash
- [ ] No Expando in product runtime

## File ownership

| Edit | Do not edit |
|------|-------------|
| `Poly.Mcp/Tools/RuntimeTool.cs` | Interpretation dictionary emit |
| MCP smoke tests | `PolicyEvaluator` product promotion |

## Status

**Status:** Not Started
