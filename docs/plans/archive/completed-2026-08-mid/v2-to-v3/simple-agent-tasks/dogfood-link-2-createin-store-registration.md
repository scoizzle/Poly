# link-2 — Register `create in` children in InstanceMap

**Suite:** [`dogfood-link-README.md`](dogfood-link-README.md)  
**Source findings:** S2-B2 (create-in children not registered in store)  
**Difficulty:** Small  
**Status:** `[x]`

## What was done

After `invoke_action` successfully executes effects (including `create in Relationship { ... }`), any newly created child instances are now registered in the session's `InstanceMap`.

**Implementation:** In `RuntimeTool.InvokeAction`, after calling `instance.InvokeAction(...)` and confirming `result.Succeeded`, the code iterates `instance.CreatedChildren`. For each child not already in `InstanceMap`, it generates a new ID and calls `McpSessionStore.TryModifyInstances` to register the child in both `DomainInstanceStore` (for link tracking) and `InstanceMap` (for MCP visibility).

**Fail-closed:** Registration failures do not silently swallow errors — `TryModifyInstances` lock safety is preserved.

**2 tests added:**
| Test | What it verifies |
|------|-----------------|
| `InvokeAction_WithCreateIn_RegistersChildInInstanceMap` | `list_instances(Loan)` returns 1 instance after create-in (was 0 before) |
| `InvokeAction_WithCreateIn_ChildIsGetInstanceAble` | Child created by create-in is accessible via `get_instance` |

## Files changed

- `Poly.Mcp/Tools/RuntimeTool.cs` — child registration after `InvokeAction` success
- `Poly.Tests/Mcp/McpSmokeTests.cs` — 2 tests

## Verification

```bash
dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj
dotnet run --project Poly.Tests/Poly.Tests.csproj
```

Total: 1626 passed (2 new).

