# create-create-in-1 — Store.Create / Store.CreateIn

**Difficulty:** M  
**Status:** `[ ]`  
**Prereq:** none (unique Store bind already on trunk of this stream)

## Objective

Create and create-in invoke a bound Store, Notify-shaped, the same way unique assign invokes `EnsureUnique`. The operation AST does not call `CreateChildInstance`.

## Required reading

1. Parent locks L1–L10 — [`../create-create-in-simulate.md`](../create-create-in-simulate.md)
2. Unique pattern — `Poly.Tests/DomainModeling/Lowering/StoreBindUniqueTests.cs`, `EffectLoweringPass.Assign`, `DomainInstanceStore.EnsureUnique`, `DomainEntityInstance.EnsureUnique`
3. Storage bag — `StorageMappingMetadata` / `StorageModel` (navs, FKs). Facts fallback when the bag is absent.
4. Do **not** read pack-host, mut-safety, or dict-sqlite plans.

## Exact steps

1. Write one failing TUnit test first (`Method_Condition_ExpectedResult`):
   - Runtime create-in (`LowerStageTransitions: false`) lowers to `Invoke(Member(This, "CreateIn"), …)` (or `Create` for by-type), **not** `CreateInNav` / `CreateByType`.
   - `DomainInstanceStore.CreateIn` registers the child and records the relationship link; unique collision returns `DomainResult.Failure` without registering.
2. Add public Store jobs: `Create` / `CreateIn` returning `DomainResult` (child on success). Constraint checks that already live on the entity factory stay there; uniqueness and graph wiring belong on Store.
3. Notify-shaped instance methods that delegate to the bound Store. List them on the type def like `EnsureUnique` / `Notify` (`BuildTypeDefNode`). Dictionary `This` cannot Member-read `Store`.
4. Runtime lowering (`!LowerStageTransitions`) emits those invokes. Read `StorageMappingMetadata` for nav/FK; fall back to relationship facts when the bag is absent. **Do not** embed bag types in the tree.
5. C# export (`LowerStageTransitions: true`) may still print `Stay.Create` / `this.CreateNav` — persistence surface until an EF Store exists. Same split as unique indexes. Do not add a new flag.
6. Keep `ExecuteStructured` working until slice 2. Do not delete it here unless create-in no longer hits it and tests stay green.

## Verification

```bash
dotnet run --project Poly.Tests/Poly.Tests.csproj -p:NuGetAudit=false -- --treenode-filter "/*StoreBind*"
dotnet run --project Poly.Tests/Poly.Tests.csproj -p:NuGetAudit=false
```

- [ ] New lowering-shape + Store-link tests pass
- [ ] Existing unique-in-if and create Failure-without-prior-mutate tests still pass (`ActionEntityReturnTests`, `StoreBindUniqueTests`)
- [ ] Tree does not name `StorageMappingMetadata`
- [ ] CORE / ADR only if the public Store job is new — parent already named it; update CORE if `DomainInstanceStore` public surface is a listed mechanism

## File ownership

| Edit | Do not edit |
|------|-------------|
| `Poly/DomainModeling/Runtime/DomainInstanceStore.cs` | `Poly.Mcp/**` (slice 5) |
| `Poly/DomainModeling/Runtime/DomainEntityInstance*.cs` (bind + type def) | C# `Stay.Create` export shape (unless a test forces a compile break) |
| `Poly/DomainModeling/Lowering/EffectLoweringPass.cs` (runtime create arms) | `ExecuteStructured` deletion (slice 2) |
| `Poly.Tests/DomainModeling/Lowering/*` | Quantifier preprocess (slice 4) |

## Status

**Status:** Not Started
