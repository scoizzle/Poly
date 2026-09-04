# create-create-in-2 — Always LowerActionBody

**Difficulty:** M  
**Status:** `[x]`  
**Prereq:** task 1

## Objective

Every action effect list compiles as one Syntax tree and runs through `Interpreter`. Delete `ExecuteStructured` as shipped meaning.

## Required reading

1. Parent success + L3 / L9 / L10
2. `DomainEntityInstance.ExecuteEffectList` / `ExecuteEffect` / `ExecuteStructured`
3. Pin tests: `Poly.Tests/DomainModeling/ActionEntityReturnTests.cs` (Failure without prior mutate; create-in unique; condition-drift)

## Exact steps

1. Write or tighten a failing test that mixed `if` + create-in / relationship-coupled `CreateEntityInstance` must compile via `LowerActionBody` (one `Interpreter.CompileChecked`), not `ExecuteStructured`.
2. Remove runtime gates `RequiresDirectExecution` and `HasEffectDependentConditionalCreate`. `ExecuteEffectList` always lowers.
3. Fail-before-mutate of illegal create (Occupied bump then pattern-fail child) belongs in the tree: Store.Create fails without registering, and/or a probe prefix in the lowered body. Do **not** keep `PrevalidateUnconditionalCreates` as a bag eval before the program.
4. Delete `ExecuteStructured`. `CreateChildInstance` is not a shipped interpreter — it may exist only as the body of Store.Create.
5. Do not pre-probe with `TryLowerVmNode` for the “can we lower?” gate — `StageTransition` mutates `_sourceStageName` (OnExit tests). Gate is: lower or throw.
6. Update `docs/interpretation/domain-execution-model.md` in this change: no Effect-IR walk at runtime.

## Verification

```bash
dotnet run --project Poly.Tests/Poly.Tests.csproj -p:NuGetAudit=false -- --treenode-filter "/*ActionEntityReturn*"
dotnet run --project Poly.Tests/Poly.Tests.csproj -p:NuGetAudit=false
```

- [x] `ExecuteStructured` has no callers (or the type is gone)
- [x] Failure-without-prior-mutate still green
- [x] Sequential stage OnExit / SourceStageName tests still green
- [x] domain-execution-model matches

## File ownership

| Edit | Do not edit |
|------|-------------|
| `Poly/DomainModeling/Runtime/DomainEntityInstance.cs` | Quantifier rewrite engine (slice 4) |
| `Poly/DomainModeling/Runtime/DomainEntityInstance.HostAbi.cs` | MCP tools |
| `docs/interpretation/domain-execution-model.md` | `Stay.Create` goldens (slice 3 may change factories; leave export goldens unless they compile-break) |

## Status

**Status:** Done 2026-09-03
