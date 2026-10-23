# TASK IS0f - the per-analysis type registry leaves the process-wide analyzer

Status: implemented. Branch `slice/IS0f`. Lane B (`analysis`). Issue #261.
Plan: [IS-README](../IS-README.md).

## Why

`TypeDefinitionNodeAnalyzer` stores the per-analysis AST type table in an instance field (`Poly/Interpretation/Analysis/Semantics/TypeDefinitionNodeAnalyzer.cs:17`) while the analyzer instance is a process-wide `static readonly` singleton (`Interpreter.cs:20`). `Analyze` assigns the field on first use (`:25`) and the provider methods answer from it (`:54,60,63,69`). Its own doc comment, 60 lines below, says the registry lives on `AnalysisContext` precisely so a shared analyzer cannot leak or clear types across analyses (`:86-90`).

The correct shape already exists: `AstTypeRegistry` implements `ITypeDefinitionProvider` and is stored as `context` metadata. The analyzer's provider methods are the only thing still reading the stale field. Two concurrent analyses overwrite it; the last one wins the queries.

Production blast radius is small — `Interpreter.Compile` passes an explicit provider (`Interpreter.cs:64`) — which is why the fix is cheap now and an intermittent wrong-type bug later.

## Scope

- Delete `_registry` and every method that reads it. The analyzer's per-analysis state is the `AnalysisContext` registry, which it already populates.
- Give callers the per-analysis provider from the analysis result instead of the analyzer: `AnalysisResult.TypeDefinitions` returning the stored registry as `ITypeDefinitionProvider` (`AnalysisResult.cs:19` already exposes typed metadata access; `AstTypeRegistry` is internal, so surface it as the interface).
- The `GetTypeDefinition(Type type)` overload answers from `ClrTypeDefinitionRegistry.Shared`, which is not per-analysis state. Keep that answer available (on the same per-analysis provider surface or as a static call), but do not let it keep the analyzer in the provider role.
- Update the consumers: `Poly.Tests/Interpretation/InterpretationStabilizationTests.cs:159,176` and `Poly.Tests/Interpretation/TypeDefinitionNodeAnalyzerTests.cs:293,314,334,353,373,393,449,457`.
- Check `AstTypeDefinition.ResolutionProvider` (`:146,159,178`) is the context's registry, not the deleted field.

## Premise correction (2026-10-22)

The claim that was false: the issue's "production blast radius today is small: `Interpreter.Compile` passes an explicit provider, so the field-backed queries are exercised mainly by tests." They are exercised in production. `DomainEntityInstance` keeps `TypeDefinitionNodeAnalyzer` as its type provider (`_typeDefAnalyzer`, `_bindingTypeProvider`, the `ExecuteEffectList` parameter, `BuildTypeDefAnalyzer`, `ModuleAwareTypeProvider`) and hands it to production compile paths. Removing the provider surface from the analyzer therefore breaks production, not just tests. Measured on the unmodified tree with only the design's lane-B half applied:

```
$ dotnet build Poly/Poly.csproj
Poly/DomainModeling/Runtime/DomainEntityInstance.HostAbi.cs(405,43): error CS1503: Argument 1: cannot convert from 'Poly.Interpretation.Analysis.Semantics.TypeDefinitionNodeAnalyzer' to 'Poly.Introspection.ITypeDefinitionProvider'
Poly/DomainModeling/Runtime/DomainEntityInstance.cs(203,29): error CS1503: Argument 3: cannot convert from 'Poly.Interpretation.Analysis.Semantics.TypeDefinitionNodeAnalyzer' to 'Poly.Introspection.ITypeDefinitionProvider'
Poly/DomainModeling/Runtime/DomainEntityInstance.cs(505,57): error CS1503: Argument 1: cannot convert from 'Poly.Interpretation.Analysis.Semantics.TypeDefinitionNodeAnalyzer' to 'Poly.Introspection.ITypeDefinitionProvider'
Poly/DomainModeling/Runtime/DomainEntityInstance.cs(923,51): error CS1503: Argument 1: cannot convert from 'Poly.Interpretation.Analysis.Semantics.TypeDefinitionNodeAnalyzer' to 'Poly.Introspection.ITypeDefinitionProvider'
Poly/DomainModeling/Runtime/DomainEntityInstance.cs(1046,53): error CS1503: Argument 1: cannot convert from 'Poly.Interpretation.Analysis.Semantics.TypeDefinitionNodeAnalyzer' to 'Poly.Introspection.ITypeDefinitionProvider'
```

Scope widened (Scot, 2026-10-22): the three `Poly/DomainModeling/Runtime/DomainEntityInstance*.cs` files and seven further test files join this slice. Retyping a field to `ITypeDefinitionProvider` is not a `Lower` or session-pipeline change and adds no capability to `DomainEntityInstance` — it removes a second owner of a per-analysis fact, which is this program's boundary.

Five of the added test files fall under lane C's globs (`AstMemberVmTests`, `AsyncMethodVmTests`, `InterpreterBranchVmTests`, `ResolvedTypeVmTests` under `*Vm*`; `InvokeMemberInstanceTests` under `*Invoke*`) and are explicitly authorised for this slice. `AstConstructorDefinitionTests` and `ParameterBindingTests` are under no lane glob.

## Files

- `Poly/Interpretation/Analysis/Semantics/TypeDefinitionNodeAnalyzer.cs`
- `Poly/Analysis/AnalysisResult.cs` — the provider accessor.
- `Poly/Analysis/Analyzer.cs` — fills it from the analysis context.
- `Poly/DomainModeling/Runtime/DomainEntityInstance.cs`
- `Poly/DomainModeling/Runtime/DomainEntityInstance.Runtime.cs`
- `Poly/DomainModeling/Runtime/DomainEntityInstance.HostAbi.cs`
- `Poly.Tests/Interpretation/InterpretationStabilizationTests.cs`
- `Poly.Tests/Interpretation/TypeDefinitionNodeAnalyzerTests.cs`
- `Poly.Tests/Interpretation/TypeRegistryIsolationTests.cs` — new.
- `Poly.Tests/Interpretation/AstConstructorDefinitionTests.cs`
- `Poly.Tests/Interpretation/AstMemberVmTests.cs`
- `Poly.Tests/Interpretation/AsyncMethodVmTests.cs`
- `Poly.Tests/Interpretation/InterpreterBranchVmTests.cs`
- `Poly.Tests/Interpretation/InvokeMemberInstanceTests.cs`
- `Poly.Tests/Interpretation/ParameterBindingTests.cs`
- `Poly.Tests/Interpretation/ResolvedTypeVmTests.cs`


## Shape matrix

| Input | Before | After | Test that proves it |
|---|---|---|---|
| analyze A, analyze B, ask A's provider for a type only A declares | absent (B's registry won) | present | `TwoAnalyses_DoNotShareTypeRegistries` |
| two analyses in parallel, each querying its own provider | intermittent wrong answer | each sees only its own types | `ParallelAnalyses_EachSeeTheirOwnTypes` |
| the same analysis queried twice | stable | stable | `ProviderAnswersAreStableWithinOneAnalysis` |

## Done when

- No `_registry` field; no provider method on the analyzer reads instance state.
- The isolation tests fail before the change (the sequential one will; if the parallel one is flaky by nature, assert the isolation property on the object graph instead of racing).
- Existing analyzer tests pass against the result-owned provider.
- Full suite green.

## SHIP if / NOT SHIP if

SHIP: the type table has one owner (the analysis), and the analyzer exposes only `Analyze`.
NOT SHIP: the field survives behind a static; the provider surface stays on the shared analyzer; a test is weakened to avoid the concurrency question.

## Tests

```
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/TypeRegistryIsolationTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/TypeDefinitionNodeAnalyzerTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/InterpretationStabilizationTests/*'
```
Then the full suite.

## Hand-edit

The registry is reachable from the analysis result, which is what a reader already holds; there is no second copy to wonder about.

## Needs Scot

Recommended and proceeding unless Scot objects: the provider surface moves off the analyzer entirely (the issue's option 1). Option 2 — "register the run's registry on the analyzer" — leaves the shared instance holding the last run's state, which is the bug with a shorter window.

## Log

| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
| 2026-10-22 | agent | Copilot/VS Code | `dceb4a58` | SHIP — the analyzer exposes only `Analyze`; one analysis owns its type table | RED first (below), GREEN after. `AstTypeRegistry` had to take over `ITypeDefinitionNodeProvider`, not just `ITypeDefinitionProvider`: returning the context's collection without it silently dropped the module declarations from `Interpreter.AnalysisRoot`, and 121 domain/MCP tests caught it. Full suite 4304/4304; build 0 warnings. |

### Evidence (2026-10-22)

RED — after the rebase, before any production change. The test routed provider queries through the shared analyzer, because the analysis exposed no provider of its own yet:

```
$ dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/TypeRegistryIsolationTests/*'
failed ParallelAnalyses_EachSeeTheirOwnTypes (35ms)
  TUnit.Engine.Exceptions.TestFailedException: [Test Failure] AssertionException: Expected to not be null
    at Poly.Tests.Interpretation.TypeRegistryIsolationTests.ParallelAnalyses_EachSeeTheirOwnTypes() … TypeRegistryIsolationTests.cs:44
failed TwoAnalyses_DoNotShareTypeRegistries (35ms)
  TUnit.Engine.Exceptions.TestFailedException: [Test Failure] AssertionException: Expected to not be null
    at Poly.Tests.Interpretation.TypeRegistryIsolationTests.TwoAnalyses_DoNotShareTypeRegistries() … TypeRegistryIsolationTests.cs:27
  total: 3
  failed: 2
  succeeded: 1
```

`TwoAnalyses_DoNotShareTypeRegistries` failed exactly as the issue predicted: after Beta's analysis, the shared instance answered `GetTypeDefinition("Alpha")` with null.

GREEN — same filter, now reading `AnalysisResult.TypeDefinitions`:

```
$ dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/TypeRegistryIsolationTests/*'
  total: 3 / failed: 0 / succeeded: 3
$ dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/TypeDefinitionNodeAnalyzerTests/*'
  total: 17 / failed: 0 / succeeded: 17
$ dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/InterpretationStabilizationTests/*'
  total: 42 / failed: 0 / succeeded: 42
$ dotnet run --project Poly.Tests/Poly.Tests.csproj
  total: 4304 / failed: 0 / succeeded: 4304
$ dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj
  0 Warning(s), 0 Error(s)
```

### Deviations from the card

1. **The provider surface is a collection, not the bare registry.** `AnalysisContext.TypeDefinitions` already stacks the AST table over the caller's providers and the CLR fallback, and still answers `GetTypeDefinition(Type)`. `AnalysisResult.TypeDefinitions` returns that collection, so one object answers name, `Type`, and node-shaped questions.
2. **`AstTypeRegistry` gains `ITypeDefinitionNodeProvider` as well as `ITypeDefinitionProvider`.** The analyzer's node-provider role was load-bearing, not incidental: `Interpreter.AnalysisRoot` asks the compile's provider for its declarations. A collection holding only the type-provider interface silently compiled the domain bodies without their type declarations — 121 domain/MCP failures whose message was `Member 'Create' on a type name is not a resolved static method`. The registry owns both halves now, because it is the per-analysis table.
3. **Eight test files pass the analyzer as a provider, not seven.** `SuspendResumeVmTests` was missed when the widened list was drawn up; it is the same one-line helper flip.
4. **`AstConstructorDefinitionTests` drops `typeDefinitions: tda`.** It was passing the same instance that the builder runs as a pass; before the pass ran, the field-backed provider answered null, so the argument carried nothing. `.Analyze(root)` is the honest call.
5. **One full-suite run of three reported a single failure** (4303/4304) before the registry took the node-provider role; the two runs after that fix were 4304/4304. The failed run's report was overwritten, so the case is unnamed; the suite runs four tests at once (`TestParallelLimit`).
