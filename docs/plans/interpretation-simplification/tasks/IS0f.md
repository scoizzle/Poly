# TASK IS0f - the per-analysis type registry leaves the process-wide analyzer

Status: planned. Branch `slice/IS0f`. Lane B (`analysis`). Issue #261.
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

## Files

- `Poly/Interpretation/Analysis/Semantics/TypeDefinitionNodeAnalyzer.cs`
- `Poly/Analysis/AnalysisResult.cs` — the provider accessor.
- `Poly.Tests/Interpretation/InterpretationStabilizationTests.cs`
- `Poly.Tests/Interpretation/TypeDefinitionNodeAnalyzerTests.cs`
- `Poly.Tests/Interpretation/TypeRegistryIsolationTests.cs` — new.

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
