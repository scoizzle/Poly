# P3-0 inventory — action return surface (2026-08-06)

## Authoring

| Layer | Behavior |
|-------|----------|
| Parse | `-> TypeName` → `InvocationResult` with one member `Instance`? No — member type is TypeName (printer uses first member type) |
| Action | `Action.Result` is `InvocationResult` (void = empty Members) |
| Create effects | `CreateEntityInstance` / create-in (when resolved) set `Effect.Result` with member `Instance` of target type |
| Export | C# DomainResult messaging |

## Runtime (before P3)

| Layer | Behavior |
|-------|----------|
| `InvokeAction` | Returns `ActionInvocationResult`: Succeeded, NewStage, FailedGuards, ErrorMessage — **no result value** |
| Created children | Tracked on instance; MCP registers them after invoke |

## MCP (before P3)

| Tool | Fields |
|------|--------|
| `invoke_action` | actionName, succeeded, newStage, failedGuards, errorMessage |

## Chosen vertical (P3)

**Entity return from create / create-in:**

```poly
PlaceOrder: action -> Order {
  create in orders { Total: 100 }
}
```

- Analysis: non-void `-> T` requires a create/create-in that produces entity type `T`.  
- Runtime: last child created in this invoke of type `T` is the return instance.  
- MCP: `returnInstanceId` when that child is registered; `returnTypeName` always when set.  
- **Not in vertical:** primitive `-> Number` from assign, last-expression-is-return.
