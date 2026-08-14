# pack-3b-1 — IContractProducer from a Domain

**Claimed by:** fleet agent pack-3b-1  
**Difficulty:** M  
**Status:** `[x]`  
**Prereq:** pack-3b-README  

## Objective

`ImportedContract Produce(Domain source)` copies published value types + façade/entry-shaped actions as outbound operations. No child entities.

## Exact steps

1. Failing test: billing domain with `ChargeRequest` value + `Ledger.Charge` action → contract named from source, types contain ChargeRequest, endpoints contain Charge, **no** Ledger entity.
2. `[NEW]` `IContractProducer` + `InternalDomainProducer`.
3. v1: project **value types** on `Domain.Types` and **actions** (document: all actions as singleton operations for v1, **or** only actions on a single façade entity if you can detect one — pick **all actions** and note instance-bind is later).
4. Do not add `import` keyword. Do not merge entities.

## Verification

```bash
dotnet run --project Poly.Tests/Poly.Tests.csproj --treenode-filter "/*/*/*/*/*InternalDomain*"
```

- [x] Types + endpoints only  
- [x] Suite green  

## File ownership

| Edit | Do not edit |
|------|-------------|
| `[NEW] Poly/DomainModeling/Packs/InternalDomainProducer.cs` (and interface) | `MinimalApiGenerator.cs` |
| `[NEW]` tests | OpenAPI |

## Status

**Status:** Done — producer + interface + 7 tests (2148 → 2154). Slice gate (pr1) is the next step per pack README.
