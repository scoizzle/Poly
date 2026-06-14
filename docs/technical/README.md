# Technical Audits — Review Results

This directory contains technical deep-dives of each Poly subsystem. Each document was reviewed item-by-item against this standard:

> "Dead code" means a value or method that is **computed or defined but the result is never consumed by any code path**. If the code is part of a coherent, self-contained system that produces a data model, and the unused parts are simply parts of that model that no current consumer queries, it is considered **dormant infrastructure** — not dead code — and is kept.

## What Was Changed

| Change | Subsystem | Lines | Rationale |
|---|---|---|---|
| Delete `AnalysisSettingsExtensions.cs` | Syntax | 22 | Convenience wrappers, not a data model. Nobody called them. |
| Remove `AnalyzerBuilder.WithOptions()` | Syntax | 6 | One method, never called. Builder creates correct default. |
| Remove dead `AnalysisContext` helpers | Syntax | 12 | Internal helpers for an early-exit signal that had no signaler. |
| Delete `StackDepthAnalyzer` + registration | Analysis | 163 | Full pass whose output was never consumed. VM now gets the same benefit from a µop-level scan. |
| Add `ComputeMaxDepth` + `Reserve` in `ProgramCompiler` | VM | +14 | Stack pre-allocation from µop scan, eliminating `Grow()` cold path. |
| **Total removed** | | **~206 lines net** | |

## What Was Reviewed and Kept (Dormant Infrastructure)

Every other item from the original audits was determined to be part of a coherent, self-contained system. The unused surface is dormant — not dead. Details in each sub-document.

## Cross-Cutting Themes

### Dormant vs Dead

The initial audit confused these two categories. Dormant infrastructure (unqueried data within a complete data model) is worth keeping. Genuinely dead code (computed results that no code path consumes, not part of a coherent model) is worth removing.

### Correctly Identified Dead Code Removed

- Convenience extensions with no callers
- Single methods never invoked
- Internal helpers for a signal that had no signaler
- A complete analysis pass whose output was never consumed, replaced by a faster µop-level alternative

### Correctly Kept Dormant Infrastructure

- Metadata fields that complete a data model but aren't currently queried
- Interface methods required for interface compliance
- Methods that maintain API symmetry (e.g., `UnsafeSet` alongside `UnsafeGet`)
- Full analysis passes whose data model anticipates future consumers
