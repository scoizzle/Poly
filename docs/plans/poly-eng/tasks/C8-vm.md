# TASK C8-vm - VM analysis gate (four analyzer gaps, five real bugs, gate Emit)

Status: planned. Branch slice/c8-vm-plan. Lane B. Review 2. Implement mill: Grok (review on OpenCode).

## Scope
Depends on C8-tc2 (C8-tc is split into tc1–tc3; Foreman 2026-10-10: implement after tc2): it deletes `DomainToCSharpExporter.StoreBind.cs` and the K0 string-named create jobs behind ~71% of the 1988 VM errors, and it moves `DomainToCSharpExporter.cs`/`.Actions.cs`. **Implement after C8-tc2 merges and re-check every symbol below** (lines are at master `60089ef0`; findings measured at `67427b72`). Evidence: [`research/2026-10-10-analysis-review.md`](../research/2026-10-10-analysis-review.md) (the raw harness output stayed on the bot box and is not in the repo).
(1) Four VM-analyzer false positives: `AstTypeRegistry.Add` keys by `FullName`, so `DomainResult<T>` overwrites `DomainResult` (`TypeDefinitionNodeAnalyzer.cs:55-57`); `AstCollectionTypeDefinition` returns `Methods => []`/`Constructors => []`, so `List<T>` has no ctor or `Add` (`:848-849`); create-arg assignability in `HasPlausibleMemberOverload` (`SyntaxTypeCompatibilityAnalyzer.cs:229-258`); `ConstructorInvocationSemanticResolver` returns null when an arg type is null, so even an exact single-arg ctor match fails (`TypeAndMemberResolutionPass.cs:560-581`).
(2) Five real bugs the noise hides: subscription registration dereferences a null singular nav in the ctor (`DomainToCSharpExporter.Notify.cs:19` `AddSubscriberRegistrationNodes`, wired at `DomainToCSharpExporter.cs:743-746`); `_automaticStageChain` is emitted non-nullable but compared and assigned null (`DomainToCSharpExporter.Actions.cs:1052-1057, 1068-1076, 1096-1098`); unknown bare identifiers in assign values pass domain analysis and lower to `this.now` (`ExpressionTypeAnalyzer.cs:632-635` tolerated by `:513-519`); the unused-parameter hint skips `CreateEntityInRelationshipEffect` initializers (`EffectAnalyzer.cs:429-431`); the orphan warning rejects non-root parents (`OwnershipAggregatePass.cs:105`).
(3) Gate: call `ThrowIfVmHasErrors` from `Emit` (`DomainSession.cs:273-300`, helper `:385`); drop the 15-sample allowlist and the smoke.poly 9-error pin; compile every maintained probe with Roslyn warnings-as-errors.

## Files
- `Poly/Interpretation/Analysis/Semantics/TypeDefinitionNodeAnalyzer.cs` (`AstTypeRegistry.Add`, `AstTypeRegistry.GetTypeDefinition`, `AstCollectionTypeDefinition.Methods`/`.Constructors`)
- `Poly/Interpretation/Analysis/Semantics/SyntaxTypeCompatibilityAnalyzer.cs` (`HasPlausibleMemberOverload`)
- `Poly/Interpretation/Analysis/Semantics/TypeAndMemberResolutionPass.cs` (`ConstructorInvocationSemanticResolver.ResolveConstructor`)
- `Poly/DomainModeling/Analysis/ExpressionTypeAnalyzer.cs` (`CheckCompatible`, `InferType`)
- `Poly/DomainModeling/Analysis/EffectAnalyzer.cs` (`CollectFromEffect`)
- `Poly/DomainModeling/Analysis/OwnershipAggregatePass.cs` (`ResolveChild`)
- `Poly/DomainModeling/Lowering/DomainToCSharpExporter.Notify.cs` (`AddSubscriberRegistrationNodes`)
- `Poly/DomainModeling/Lowering/DomainToCSharpExporter.Actions.cs` (`AddAutomaticTransitionLoopGuard`)
- `Poly/DomainModeling/Compile/DomainSession.cs` (`Emit`)
- `Poly.Tests/Interpretation/TypeDefinitionNodeAnalyzerTests.cs`, `Poly.Tests/Interpretation/InvalidProgramTests.cs`, `Poly.Tests/Interpretation/AstConstructorDefinitionTests.cs`
- `Poly.Tests/DomainModeling/Analysis/UnknownNameAnalysisTests.cs`, `Poly.Tests/DomainModeling/Analysis/EffectAnalyzerFailClosedTests.cs`, `Poly.Tests/DomainModeling/Analysis/OwnershipAggregateAnalysisTests.cs` (new)
- `Poly.Tests/DomainModeling/Compile/VmAnalyzerReportTests.cs`, `Poly.Tests/DomainModeling/Compile/AnalysisReportArtifactTests.cs`, `Poly.Tests/DomainModeling/Compile/EmitRefusesVmErrorsTests.cs`, `Poly.Tests/TestHelpers/ExportedCSharp.cs`
- `docs/domain-modeling/pipeline-convergence-plan.md` (append-only card) and `pipeline-convergence-plan.decisions.md`; PR 155 edits the plan doc too, so keep edits small.

## Shape matrix
| Input kind | Before | After | Test that proves it |
|---|---|---|---|
| `DomainResult` and `DomainResult<T>` in one module (V1) | second `Add` overwrites the first; `Order.Pay` reports "no matching member 'Success' on 'DomainResult'" | both registered; each resolves by name + arity | `TypeDefinitionNodeAnalyzerTests` |
| `List<X>` ctor / `Add` / `Count` (V2) | `Methods => []`, `Constructors => []`; field init / `AttachItems` report no ctor / no `Add` | `List<X>` exposes a parameterless ctor and `Add(X)` (Count already via `_count`) | `TypeDefinitionNodeAnalyzerTests` |
| create arg assignability (V3) | `Create` with `new List<X>()`/optional args reports "no matching member 'Create' … 6 args" | assignable/nullable/optional args match; no VM error | `InvalidProgramTests` |
| `new Order(total)` exact single-arg ctor (V5) | arg type null → resolver returns null → "no matching constructor" | null arg types tolerated; exact arity/assignable ctor resolves | `InvalidProgramTests`, `AstConstructorDefinitionTests` |
| subscription on a null singular nav (R1) | ctor calls `this.Tracks.RegisterTrBSubscriber(this)` when `Tracks` is null → NRE (CS8602) | guarded registration; no NRE, no Roslyn warning | `DomainToCSharpExporterTests`; `VmAnalyzerReportTests` Roslyn row |
| automatic-transition chain field (R2) | `HashSet<string> _automaticStageChain` compared/assigned null (CS8618/CS8625) | emitted `HashSet<string>?` | `DomainToCSharpExporterTests`; `VmAnalyzerReportTests` Roslyn row |
| `assign X to now` / unknown bare identifier | passes domain analysis, lowers to `this.now` (CS1061) | domain Error (`SemanticTypeCompatibility`) | `UnknownNameAnalysisTests` |
| `create in rel { Prop: value }` (D1) | initializer's `ParameterAccess` not collected → DMEFF004 hint misfires | initializers walked like `CreateEntityInstance`; hint only on real unused params | `EffectAnalyzerFailClosedTests` |
| grandchild entity whose parent is non-root (D2) | `ResolveChild` skips non-root parents → DMAGG001 orphan warning | non-root parent accepted; no warning | `OwnershipAggregateAnalysisTests` |
| any sample domain (gate) | `Emit` fail-open; 15-sample VM-error allowlist; smoke.poly pinned at 9 errors | `Emit` calls `ThrowIfVmHasErrors`; 0 VM errors; 0 Roslyn warnings | `VmAnalyzerReportTests`, `AnalysisReportArtifactTests`, `EmitRefusesVmErrorsTests` |

## Done when
- The four analyzer gaps are fixed: `DomainResult`/`DomainResult<T>` coexist by name + arity; `List<T>` has a ctor and `Add`; create args assignable; exact ctor match resolves.
- The five real bugs are fixed: subscription registration is null-safe (R1); `_automaticStageChain` is `HashSet<string>?` (R2); unknown bare identifiers in assign values are a domain Error; `create in` initializers count as parameter references; grandchildren get an aggregate parent.
- `Emit` calls `ThrowIfVmHasErrors` and fails closed on any VM error; `VmAnalyzerReportTests` has no VM-error allowlist; the `smoke.poly` 9-error pin is gone.
- **0 VM errors and 0 Roslyn warnings on every maintained sample** (`docs/probes/**`, `demo/live/**`; the invalid-by-design `nested-invoke-type-mismatch.poly` stays a domain Error and is excluded from Emit). Every maintained probe compiles with Roslyn warnings-as-errors.
- Archive probes (`docs/plans/archive/**`) re-checked on current syntax: those that fail only on the four gaps are fixed; unmaintained ones that still fail are deleted or listed in the PR body.
- Full suite green.

## SHIP if / NOT SHIP if
SHIP if every maintained sample emits with 0 VM errors and compiles with 0 Roslyn warnings, `Emit` throws on a new VM error, the allowlist and 9-error pin are gone, and the full suite passes.

NOT SHIP if any maintained sample still has a VM error or Roslyn warning, `Emit` stays fail-open, the allowlist/pin survives, a gap is fixed by widening the analyzer's tolerance instead of keying/resolving correctly, or an emitted C# golden is hand-edited.

## Tests
```
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/TypeDefinitionNodeAnalyzerTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/InvalidProgramTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/AstConstructorDefinitionTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/UnknownNameAnalysisTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/EffectAnalyzerFailClosedTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/OwnershipAggregateAnalysisTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/DomainToCSharpExporterTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/EmitRefusesVmErrorsTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/VmAnalyzerReportTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/AnalysisReportArtifactTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/EmitGoldenTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj
```

## Hand-edit
Each fix is a local edit at a named symbol (registry key, collection members, assignability test, ctor resolver, null guard, nullable field, unknown-name check, effect walk, parent check, one `ThrowIfVmHasErrors` call) so Scot can open any touched spot cold.

## Needs Scot
none

## Log
| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
| 2026-10-10 | planner | opencode/opencode-go/deepseek-v4.1-flash | 60089ef0 | planned | - |
| 2026-10-11 | implementer | grok/grok-4.6 | 1d74665c | pushed | three analysis bugs only (unknown assign, create-in params, grandchild parents); tests 3541/3541; Today spelling; CRM host goldens regenerated |
