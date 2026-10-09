# TASK C2a - Create initializers without re-lowering
Status: planned. Branch slice/c2a-create-initializers. Lane B. Review 2. Implement mill: Grok (review on OpenCode).

## Scope
Compiled create trees already invoke `Create` / `CreateIn` / `ProbeCreate` with VM-evaluated values (`EffectLoweringPass.LowerRuntimeFactoryCall` at `Poly/DomainModeling/Lowering/EffectLoweringPass.cs:1098-1148`; `RuntimeCreateFactory` at `Poly/DomainModeling/Runtime/DomainEntityInstance.HostAbi.cs:495-528`). When `Store` is set, those jobs call `DomainInstanceStore` (`HostAbi.cs:556-560, 571-575, 586-590`). When `Store` is null they rebuild `PropertyBinding` literals (`ValuesAsLiteralBindings` `:605-611`) and re-lower them: `PrevalidateCreateInitializers` `:618-642` (`new DomainExpressionLoweringPass` at `:630`) and `CreateChildInstance` `:651-769` (`new DomainExpressionLoweringPass` at `:693`), reached from `Create` `:562` and `CreateIn` → `ExecuteCreateInRelationship` `:577-579, 851-878`. Those two constructions are the only `new DomainExpressionLoweringPass` under `Runtime/`.
Attach a default internal `DomainInstanceStore` only inside those three jobs when `Store` is null (`new DomainInstanceStore().Add(this)` via the existing `internal set` at `DomainEntityInstance.cs:32`), then always take the store path. Delete the no-store fallbacks and the now-dead helpers (`ValuesAsLiteralBindings`, `PrevalidateCreateInitializers`, `CreateChildInstance`, `ExecuteCreateInRelationship`, `TryEvalActionParamPath`, `ContainsRelationshipNavigation`). Comment the default store as the C8d named twin. Do not attach at `DomainEntityInstance.Create` (`:82-145`): that would give every instance a store and change `EnsureUnique` / quantifier no-store behavior. Leave `FillCreateDefaults` / `ValidateCreateConstraints` for C2b (`DomainInstanceStore.cs:149-150, 174-175`). No new type, no public factory.

## Files
- `Poly/DomainModeling/Runtime/DomainEntityInstance.HostAbi.cs` (`Create` `:556`, `CreateIn` `:571`, `ProbeCreate` `:586`; delete the fallbacks)
- `Poly/DomainModeling/Runtime/DomainInstanceStore.cs` (comment `:224-225` still says `CreateChildInstance`)
- `Poly.Tests/DomainModeling/Lowering/StoreBindCreateTests.cs` (new store-less create-with-initializer test)
- `Poly.Tests/DomainModeling/Lowering/Item4FailBeforeMutateTests.cs` (must stay green; store-less probe is in scope)
- `Poly.Tests/DomainModeling/DomainEntityInstanceTests.cs` (`CreateEntityInstance_RelationshipNameWithoutStore_NoOp` `:2229`, `CreateEntityInRelationship_WithoutStore_NoCrash` `:2677`, comments `:2013, :2698`)
- `Poly.Tests/DomainModeling/Runtime/DomainInstanceHostJobsTests.cs` (pairs still land; no public API added)
- `Poly.Tests/DomainModeling/Lowering/StoreBindUniqueTests.cs` (`UniqueAssign_WithoutStore_SucceedsWhenNoPeers` `:181` must stay green)

## Shape matrix
| Input kind | Before | After | Test that proves it |
|---|---|---|---|
| Store-less `create Type { Prop: expr }` | `Create` `:559-565` wraps values as literals and `CreateChildInstance` re-lowers them (`:693-700`) | Default internal store; `Store.Create` (`DomainInstanceStore.cs:85-93`); initializer already evaluated by the tree | new `StoreBindCreateTests` store-less create-with-initializer |
| Store-less `create in Rel { Prop: expr }` | `CreateIn` `:574-580` → `ExecuteCreateInRelationship` `:851-878` → `CreateChildInstance` | `Store.CreateIn` (`:99-123`) | same class; update `CreateEntityInRelationship_WithoutStore_NoCrash` |
| Store-less `ProbeCreate` | `ProbeCreate` `:589-602` → `PrevalidateCreateInitializers` `:630-637` | `Store.ProbeCreate` (`:129-152`) | `Item4FailBeforeMutateTests`; new store-less probe row |
| Create already on a store | `Store.Create` / `CreateIn` / `ProbeCreate` | unchanged | existing `StoreBindCreateTests` |
| `new DomainExpressionLoweringPass` under `Runtime/` | two sites, `HostAbi.cs:630` and `:693` | none | `git grep 'new DomainExpressionLoweringPass' -- Poly/DomainModeling/Runtime` empty |
| Default store | none | private lazy attach; `Store` stays `public get; internal set`; no public factory or type | new test: after store-less create, `Store` is a `DomainInstanceStore` and no new public member exists |
| Unique assign / quantifier with no create and no store | `EnsureUnique` Success when `Store is null` (`HostAbi.cs:40-41`); quantifier throws | unchanged (do not attach at construction) | `StoreBindUniqueTests.UniqueAssign_WithoutStore_SucceedsWhenNoPeers`; `DomainEntityInstanceTests.EvaluatePolicy_Quantifier_WithoutStore_Throws` |

## Done when
- `StoreBindCreateTests` and `Item4FailBeforeMutateTests` pass.
- New test: store-less create with an initializer (child carries the initializer value; no test-side `store.Add` before invoke).
- `git grep 'new DomainExpressionLoweringPass' -- Poly/DomainModeling/Runtime` is empty.
- `git grep PrevalidateCreateInitializers -- Poly Poly.Tests` and `git grep CreateChildInstance -- Poly Poly.Tests` are empty (touched comments updated).
- Default store is not public API. `FillCreateDefaults` / `ValidateCreateConstraints` still exist (C2b). Full suite green.

## SHIP if / NOT SHIP if
SHIP if no `DomainExpressionLoweringPass` is constructed under `Runtime/` for creates and the store-less initializer test passes.

NOT SHIP if the default store is public API, if it is attached in `DomainEntityInstance.Create`, if `EnsureUnique` / quantifier no-store tests go red, or if C2b's store twins are deleted here.

## Tests
```
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/StoreBindCreateTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/Item4FailBeforeMutateTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/DomainEntityInstanceTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/DomainInstanceHostJobsTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/StoreBindUniqueTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/ParityTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj
```

## Hand-edit
Three jobs grow one lazy `Add(this)` and the no-store re-lower methods are deleted; Scot can open `HostAbi.cs` cold.

## Needs Scot
none

## Log
| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
| 2026-10-09 | planner | grok/grok-4.6 | `71fef08f` | planned | - |
