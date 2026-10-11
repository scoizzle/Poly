# TASK C8-tc1a - Lowering emits typed by-type create calls (K0)

Status: planned. Branch `slice/c8-tc1a`. Lane B. Review 2.
Split from C8-tc1 on 2026-10-10 by the local agent, because C8-tc1 needed
[`EffectLoweringPass`] + the exporter helper surface + the interpreter
static-factory bind (6 source files, well past the ~3-file rule in
[`LOCAL-AGENT.md`](../LOCAL-AGENT.md) §5). C8-tc1b is `create in`.

Parent: [`tasks/C8-tc1.md`](C8-tc1.md). Plan card: [`pipeline-convergence-plan.md` **C8-tc**](../../../domain-modeling/pipeline-convergence-plan.md).

## Scope
Finding K0 (lowering half) from [`research/2026-10-10-codegen-review.md`](../research/2026-10-10-codegen-review.md).
Lowering still emits a string-named store job for by-type create:
`CreateEntityInstance` (`Poly/DomainModeling/Lowering/EffectLoweringPass.cs:897`)
calls `LowerRuntimeFactoryCall` (`:1136`), which prints
`this.Create("Fine", "Amount", 5L, …)` with a `DomainResult<object>` cast.
This slice makes *by-type* create lower to the typed static factory
`Fine.Create(5L, …defaults)` returning `DomainResult<Fine>` with no object cast.
`create in` (G1) stays string-named until C8-tc1b; probes and `EnsureUnique`
stay as they are until C8-tc2.

The typed factory already exists in the printed C#: static `Entity.Create(…)`
(`DomainToCSharpExporter.cs`, near `:805`; params `GetConstructorParameters`
plus `TrailingDefaultedProperties`). Under simulate the same lowered tree runs
in the interpreter, so `Fine.Create` must bind onto the dictionary-backed
instance: static `Create`/`ProbeCreate` bodies are rewritten to
`DomainInstanceHostJobs.TypedCreate(typeName, values)` using the ambient subject
(the interpreter thread's current instance).

## Files
- `Poly/DomainModeling/Lowering/EffectLoweringPass.cs` (`CreateEntityInstance` lowers to a typed `Type.Create` call via a new `LowerTypedCreateCall` / `BuildFullCreateArgs` / `ArgumentFor` / `LowerInitializerValue` / `BindingsByName` / `TypeDefaultNode`. `LowerRuntimeFactoryCall` stays for `create in` and probes.)
- `Poly/DomainModeling/Lowering/DomainToCSharpExporter.Notify.cs` (`GetConstructorParameters` private → internal; add `internal static TrailingDefaultedProperties(Entity, INodeMetadataProvider)` and `internal static EmptyListNode(string)`.)
- `Poly/DomainModeling/Lowering/DomainToCSharpExporter.cs` (`TypedNull` private → internal.)
- `Poly/DomainModeling/Runtime/DomainEntityInstance.cs` (push/pop an ambient subject around `ExecuteCachedSubscriptionTree` / the action-body execute; in `ModuleAwareTypeProvider` wrap the module so static `Create`/`ProbeCreate` are rebound by `BindStaticFactory`; add `BindStaticFactory`, `RewriteTypedFactories`, `FactoryDictKeys`, `IsEnumerableParam`.)
- `Poly/DomainModeling/Runtime/DomainInstanceHostJobs.cs` (add `TypedCreate(string typeName, Dictionary<string, object?> values)` routing to `Receiver(DomainEntityInstance.Ambient, nameof(Create)).Create(...)`, trimming nulls; `Values()`/`With(...)` already exist.)
- Tests: `Poly.Tests/DomainModeling/Lowering/StoreBindCreateTests.cs`, `DomainToCSharpExporterTests.cs`, `ParityTests.cs`; goldens regenerated under `Poly.Tests/DomainModeling/Compile/EmitGolden/`.
- Reference: [`research/c8-tc-wip.patch`](../research/c8-tc-wip.patch) has the working draft (`LowerTypedFactoryCall`, `BuildFullCreateArgs`, `BindStaticFactory`). It applies at `931b1461`, not on master; copy the hunks for by-type create only.

## Shape matrix
| Input kind | Before | After | Test that proves it |
|---|---|---|---|
| `create Fine { Amount: 5 }` | `this.Create("Fine", "Amount", 5L)`, `DomainResult<object>`, cast | `Fine.Create(5L, …defaults)` returning `DomainResult<Fine>` | `StoreBindCreateTests`; golden `simulate-create-type/*.golden` |
| same scenario simulated vs printed | agree | still agree | `ParityTests` |
| `create in …` (G1) | `this.CreateIn("…")` | **unchanged** (C8-tc1b) | existing goldens still string-named |
| probe (`ProbeCreate`) | `this.ProbeCreate("…")` | **unchanged** (C8-tc2) | existing probe goldens |

## Done when
- No golden contains a string-named `this.Create("` call; `git grep -n 'this.Create("' -- Poly.Tests/DomainModeling/Compile/EmitGolden` empty.
- The interpreter runs the typed static `Create`; `ParityTests` green.
- Goldens regenerated, not hand-edited. Full suite green.
- `CreateIn("` goldens are untouched (that is C8-tc1b).

## SHIP if / NOT SHIP if
SHIP if by-type create lowers to `Type.Create(...)`, parity holds, and no `create in` or probe change rode along.

NOT SHIP if any by-type create still lowers string-named, a golden was hand-edited, or a `CreateIn`/probe/`EnsureUnique` change rode along (t1b/tc2).

## Tests
```
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/StoreBindCreateTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/DomainToCSharpExporterTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/ParityTests/*'
POLY_UPDATE_GOLDEN=1 dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/EmitGoldenTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj
```

## Hand-edit
One lowering path for by-type create, and one name map in the interpreter type provider; Scot can open both cold.

## Needs Scot
none

## Depends
C6a, C6b1 (merged).

## Log
| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
| 2026-10-10 | handoff | - | - | split from C8-tc1 | K0 |
