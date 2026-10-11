# TASK C8-tc1a - Lowering emits typed by-type create calls (K0)

Status: implemented, BLOCKED on the C8-vm analyzer fixes (see Blocker). Branch `slice/c8-tc1a`. Lane B. Review 2.
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
`Fine.Create(5L, …defaults)` returning `DomainResult<Fine>` with no object cast,
**in an action-result context** (the enclosing action returns `DomainResult<T>`,
as in `simulate-create-type` / `simulate-create-create-in`). Probes and
`EnsureUnique` stay as they are until C8-tc2.

**Boundary (corrected 2026-10-10).** The finding-based split (K0 = "by-type",
G1 = "create-in") is *not* clean: a by-type create in a **void / subscription**
context lowers to `this.Create{Nav}(...)`, i.e. create-in machinery.
`mcp-library/Patron.cs.golden:86` is exactly that (`internal void
WhenEachLoanOverdue(Loan loan)` creates a `Fine` onto `Patron.Fines`). So the
honest seam is **context**, not finding: tc1a takes the action-result context
(`Type.Create`), and tc1b takes `create in` **plus** the void-context by-type
create (`this.Create{Nav}`).

The typed factory already exists in the printed C#: static `Entity.Create(…)`
(`DomainToCSharpExporter.cs`, near `:805`; params `GetConstructorParameters`
plus `TrailingDefaultedProperties`). Under simulate the same lowered tree runs
in the interpreter, so `Fine.Create` must bind onto the dictionary-backed
instance: static `Create`/`ProbeCreate` bodies are rewritten to
`DomainInstanceHostJobs.TypedCreate(typeName, values)` using the ambient subject
(the interpreter thread's current instance).

## Blocker (2026-10-10)
Implemented and verified for the **printer**: build clean, `StoreBindCreateTests` 16/16,
`DomainToCSharpExporterTests` 100/100, `ParityTests` 66/66, goldens regenerated (2 files,
one line each: `this.Create("Fine", …)` → `Fine.Create(5L, "TypeCreate", (Patron?)null)`),
and only the void-context `mcp-library` create stays string-named.

But the **interpreter** cannot run the typed call yet. Full suite: 3 fails
(`SimulateCreateDogfoodTests`: `TypeOnly_UnambiguousManyRel_ListsAndLinks`,
`TypeOnly_AmbiguousManyRel_ListsButDoesNotLink`, `Combined_TypeThenRel_OnOnePatron_BothLinked`),
all `VM compile rejected: AST method 'Create' forwards to a CLR static method and could
not be compiled`, with inner errors:
- `no matching member 'Create' on 'Fine' for invoke with 3 argument(s)`
- `no matching member 'Create' on 'Patron' for invoke with 2 argument(s)`
- `no matching constructor for 'List<Fine>' with 0 argument(s)`

These are the C8-vm analyzer items ("`List<T>` with no ctor/`Add`", "create-arg
assignability", "exact ctor match"). So **C8-tc1 depends on the C8-vm analyzer fixes**;
the plan's order (C8-vm after C8-tc2, LOCAL-AGENT §3 row 6) is wrong for the lowerable
path. The code on this branch is the reference: it is the `c8-tc-wip.patch` lowering,
done for the value-returning context with `RewriteTypedFactories` / `BindStaticFactory`
/ `TypedCreate`.

**Needs Scot:** fold the four C8-vm analyzer fixes into C8-tc1a, or land C8-vm's analyzer
half before C8-tc1a. Do not merge this branch until one of those happens.

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
| by-type create in a void context (`mcp-library` `WhenEachLoanOverdue`) | `this.Create("Fine", …)` | **unchanged** (C8-tc1b) | existing `mcp-library/Patron.cs.golden` |
| probe (`ProbeCreate`) | `this.ProbeCreate("…")` | **unchanged** (C8-tc2) | existing probe goldens |

## Done when
- No action-result-context golden contains a string-named `this.Create("` call; the two `simulate-create-*` goldens show `Fine.Create(...)`. (`mcp-library/Patron.cs.golden` keeps its void-context string form until C8-tc1b.)
- The interpreter runs the typed static `Create`; `ParityTests` green.
- Goldens regenerated, not hand-edited. Full suite green.
- `CreateIn("` goldens are untouched (that is C8-tc1b).

## SHIP if / NOT SHIP if
SHIP if by-type create in an action-result context lowers to `Type.Create(...)`, parity holds, and no `create in`, void-context create, or probe change rode along.

NOT SHIP if any action-result-context create still lowers string-named, a golden was hand-edited, or a `CreateIn`/void-context/probe/`EnsureUnique` change rode along (t1b/tc2).

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
The K0/G1 split is not clean (see Boundary). Two options: (a) take the
context-based seam above — tc1a is still ~5 source files (lowering + the shared
exporter helpers + the interpreter static bind + host job), tc1b is ~4; or
(b) run C8-tc1 as one ~6-file slice on a dedicated run. Recommend (a): the
static-factory bind is the risky part and lands first, verified on two goldens.

## Depends
C6a, C6b1 (merged).

## Log
| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
| 2026-10-10 | handoff | - | - | split from C8-tc1 | K0 |
| 2026-10-10 | implementer | opencode/opencode-go/deepseek-v4.1-flash | (this branch) | implemented; **BLOCKED**: printer green (goldens, 16/16, 100/100, 66/66), 3 `SimulateCreateDogfoodTests` fail on VM analyzer errors — needs the C8-vm analyzer fixes | K0 |
