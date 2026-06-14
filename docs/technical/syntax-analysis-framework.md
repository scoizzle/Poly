# Syntax / Analysis Framework — Technical Deep Dive

**Files:** `Poly/Syntax/Analysis/` (18 files, ~1,245 lines)

## Qualification Standard

Throughout this document, "dead code" means a value or method that is **computed or defined but the result is never consumed by any code path**. If the code is part of a coherent, self-contained system that produces a data model, and the unused parts are simply parts of that model that no current consumer queries, it is considered **dormant infrastructure** — not dead code — and is kept.

## Architecture

The analysis framework is a pass pipeline. `AnalyzerBuilder` collects `INodeAnalyzer` passes, `Analyzer` runs them in order over a root AST node, and each pass reads/writes per-node metadata through `AnalysisContext`.

### Key Types

| Type | Role | Lines |
|---|---|---|
| `AnalyzerBuilder` | Accumulates passes, builds `Analyzer` | 33 |
| `Analyzer` | Runs passes in foreach loop with timing | 77 |
| `AnalysisContext` | Per-run state bag (metadata, diagnostics, settings) | 171 |
| `AnalysisResult` | Immutable output wrapping context state | 81 |
| `INodeAnalyzer` | Pass contract (1 method) + tree-walk extensions | 67 |
| `INodeMetadataProvider` | Query interface (`GetMetadata<T>`) | 4 |
| `IAnalysisMetadata` | Marker interface for metadata types | 6 |
| `NodeMetadataStore` | Two-level store with inline-array promotion | 195 |
| `AnalysisOptions` | Pipeline control flags (3-value enum) | 47 |
| `AnalysisSettings` | Typed settings dictionary | 37 |
| `AnalysisDiagnosticConfiguration` | Diagnostic severity filtering | 32 |
| `AnalysisTelemetry` | Per-pass timing capture | 26 |
| `Diagnostic` | Diagnostic record + severity helpers | 67 |
| `NodeReplacementMetadata` | Node substitution for backends | 27 |
| `SyntaxDiffUtil` | Generic tree diff (snapshot + compare) | 157 |
| `IncrementalAnalysisAnalyzer` | Tree index + invalidation tracking | 161 |
| `AnalyzerVisitTrackingAnalyzer` | Cycle guard registration | 35 |

## Changes Made (verified dead, not dormant)

| What | Why | Lines |
|---|---|---|
| `AnalysisSettingsExtensions.cs` | Convenience wrappers around `Settings.Get<T>()`, not part of any data model. Nobody called them. | 22 |
| `AnalyzerBuilder.WithOptions()` | One method, never called. The builder creates the correct default. | 6 |
| `AnalysisContext.RequestEarlyExit()` | Internal helper for an early-exit signal that had no signaler. `ShouldContinue()` does the same job. | 3 |
| `AnalysisContext.ShouldContinueAnalysis()` | Redundant with `ShouldContinue()`. | 6 |
| `AnalysisContext.ShouldStopOnStructuralErrors()` | Internal helper, not part of any public contract. | 2 |
| `AnalysisContext._earlyExitRequested` | Dead field, only read/written by the above dead methods. | 1 |

## Reviewed and Kept — Dormant Infrastructure Within Coherent Systems

### `IAnalysisMetadata` (7 lines)
**What it does:** Empty marker interface constraining the metadata store generics.

**Why keep:** Type safety gate. Without it, `where TMetadata : class` allows any reference type as metadata. Seven lines for compile-time safety on every `GetMetadata<T>` call. Part of the metadata system's abstraction.

### `INodeMetadataProvider` (4 lines)
**What it does:** Interface with one method (`GetMetadata<T>`), implemented by both `AnalysisContext` and `AnalysisResult`.

**Why keep:** Enables `NodeReplacementMetadataExtensions.GetNodeReplacement()` to accept either context without caring which. Part of the metadata query abstraction.

### `NodeMetadataStore` inline arrays (195 lines)
**What it does:** Per-node metadata container with inline arrays (up to 4 entries) that promote to Dictionary only when exceeded.

**Why keep:** Hot-path optimization. `GetMetadata<T>` is called once per (node × pass). The inline arrays avoid dictionary allocation + hash computation for the common case. Justified performance optimization.

### `AggregateChildren` / `AnyChild` (31 lines)
**What it does:** Extension methods on `INodeAnalyzer` for folding over child nodes.

**Why keep:** Both called by `ControlFlowAnalysisPass`. Not dormant — actively consumed.

### `AnalysisResult.OptionsUsed` / `AnalysisWasTerminatedEarly` (2 lines)
**What they do:** Record which options were active and whether analysis was cut short.

**Why keep:** Important signal for consumers — if analysis stopped early, the result may be incomplete. Part of the analysis result's public contract.

### `AnalysisOptions.StopOnStructuralErrors` static property (1 line)
**What it does:** Convenience factory for creating options with `StopOnStructuralErrors` mode.

**Why keep:** Not dead — it's dormant infrastructure. The `ShouldStopOnStructuralErrors` property IS used by `AnalysisContext.ShouldContinue`. Making this the default would change pipeline behavior (stop at first structural error), which is a design choice, not cleanup. Remove.

### `SyntaxDiffUtil` (157 lines)
**What it does:** Generic tree diffing — snapshots and compares two ASTs by node fingerprint.

**Why keep:** One consumer (`DomainDiffUtil`), but that consumer is in the evolution layer under active development. Replacing it with a domain-specific diff before the evolution layer stabilizes is premature.

### `IncrementalAnalysisAnalyzer` duplicate walk
**What it does:** Builds a tree index, then computes affected nodes via subtree-range index + manual stack traversal.

**Why keep:** The subtree-range index and manual traversal serve different purposes. Both paths exist because invalidated nodes may not form a contiguous prefix. The `.ToHashSet()` dedup is a short-circuit union.
