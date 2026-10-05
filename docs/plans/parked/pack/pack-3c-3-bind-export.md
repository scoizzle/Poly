# pack-3c-3 — Bind is a call in export

**Difficulty:** M  
**Status:** `[x]` — Claimed by fleet agent pack-3c-3  
**Prereq:** pack-3c-2 `[x]`  

## Objective

Exported root action that is `bind`s to a contract endpoint invokes the adapter (in-process call or documented stub that fails closed if unimplemented). Not a second parse of the child domain.

## Exact steps

1. Failing test: Shop Order.Pay bound to Billing.Charge — generated C# / API handler calls through the binding, does not ignore it.
2. Store/runtime already has binding type provider — export must not drop it.
3. External contract with no implementation: fail closed or emit a not-implemented adapter — **no silent no-op**.

## Verification

```bash
dotnet run --project Poly.Tests/Poly.Tests.csproj --treenode-filter "/*/*/*/*/*Bind*"
dotnet run --project Poly.Tests/Poly.Tests.csproj --treenode-filter "/*/*/*/*/*MinimalApi*"
```

## File ownership

| Edit | Do not edit |
|------|-------------|
| export / MinimalApi bind path | OpenAPI importer |
| tests | Grammar TokenWriter |

## Status

**Status:** Done — claimed by fleet agent pack-3c-3 (2026-08-13)

**Adapter approach chosen: documented not-implemented adapter (fail-closed stub).**
The produced contract endpoint has no callable in the exported root (the child domain is
never compiled in — no second parse), so there is no in-process adapter to emit. Export
therefore:
- Prepends `{Contract}Adapters.{Endpoint}({param})` to the bound action's generated method
  (`Order.Pay` calls `BillingAdapters.Charge(request)`), so the binding is a real call —
  never a bodyless local implementation, never dropped.
- Emits one `{Contract}Adapters` class per bound contract (in the shared projection output,
  e.g. `Poly.Types.cs`) whose bound-endpoint methods **throw `NotImplementedException`**
  with a documented message until an in-process adapter is registered.
- The Minimal API handler calls `entity.Pay(dto.request)` and thus goes through the binding;
  the throw is caught by the handler's catch → HTTP 500 (fails closed).

The DSL guide now states the export contract in the Contracts section.
