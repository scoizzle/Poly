# create-create-in-3 — Unify create factories

**Difficulty:** M  
**Status:** `[ ]`  
**Prereq:** task 2

## Objective

One Store job family for create. Delete `CreateByType` / `CreateInNav` / `ProbeCreateByType` as shipped instance factories.

## Required reading

1. Parent L7 — if emit of Store.Create is hard, bind the host Store; do not add a flag
2. `RuntimeCreateFactory` / `InvokeNamed` in `DomainEntityInstance.HostAbi.cs` + `InvokeNamed.cs`
3. `EffectLoweringPass.LowerRuntimeFactoryCall`
4. Export goldens that pin `Stay.Create` / `this.CreateNav` — those are the persistence-surface print until EF Store

## Exact steps

1. Failing test: runtime-lowered create tree contains `Create` / `CreateIn` only — no `CreateByType` / `CreateInNav` / `ProbeCreateByType` string names.
2. Delete those factories from type def, `InvokeNamed`, and `LowerRuntimeFactoryCall`. Probe-before-mutate (if still required) is Store.Create that does not register, or a named Store job — not a third factory family.
3. C# export may keep `Stay.Create` / `CreateNav` until an EF Store exists (same honesty as unique → EF indexes). Document that split in CORE / domain-execution-model in this change. Do **not** introduce a new `LowerStageTransitions` meaning or a third create shape.
4. Heuristic check: project of an action body still does not consult `StorageMappingMetadata`. If it must, the collaborator was not bound — stop and bind, do not special-case the printer.

## Verification

```bash
dotnet run --project Poly.Tests/Poly.Tests.csproj -p:NuGetAudit=false
```

- [ ] No shipped `CreateByType` / `CreateInNav` / `ProbeCreateByType`
- [ ] Export goldens for `Stay.Create` still honest or updated with the documented print split
- [ ] `rg CreateByType` / `CreateInNav` / `ProbeCreateByType` is tests-of-absence or gone

## File ownership

| Edit | Do not edit |
|------|-------------|
| `Poly/DomainModeling/Runtime/DomainEntityInstance*.cs` | MCP session store |
| `Poly/DomainModeling/Lowering/EffectLoweringPass.cs` | Quantifiers |
| `docs/CORE.md` (create factory sentence) | EF / DbContext generation |

## Status

**Status:** Not Started
