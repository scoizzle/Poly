# Lowering and VM — Technical Deep Dive

**Files:** `Poly/Interpretation/VirtualMachine/` (17 files, ~2,894 lines)

## Qualification Standard

Throughout this document, "dead code" means a value or method that is **computed or defined but the result is never consumed by any code path**. If the code is part of a coherent, self-contained system that produces a data model, and the unused parts are simply parts of that model that no current consumer queries, it is considered **dormant infrastructure** — not dead code — and is kept.

## Architecture

```
AST (analyzed) → Lowering → Bytecode → ProgramCompiler → Action<VmState> → Vm.Execute
```

## Changes Made

| Change | Value | Lines |
|---|---|---|
| Added `ComputeMaxDepth` + `Reserve` in `ProgramCompiler.Compile` | VM pre-sizes its stack at startup, eliminating the `Grow()` cold path | +14 |

## Reviewed and Kept — Dormant Infrastructure Within Coherent Systems

### Bytecode dead payloads — KEEP ALL

**What they are:** Fields on `Bytecode` that are populated during lowering but never currently queried.

**`Bytecode.AnalysisResult`** — The full analysis result is carried on the `Bytecode` instance. Any consumer of `Bytecode` (VM, debugger, MCP tools, test assertions) can query metadata, diagnostics, or replaced nodes through it. Currently no one does — but it's part of the `Bytecode` data model (the complete output of lowering).

**`Bytecode.CallSiteTargets`** — Human-readable CLR method signatures. Populated at call-site creation time. Serves debugging/tooling if ever wired up.

**`Bytecode.LoopBodies`** — Loop body metadata (µop ranges, variable maps) intended for a loop optimization pass. If a loop optimization pass is ever added, `LoopBodies` provides the PC ranges and variable maps needed.

**`FunctionEntry.SourceNode`** — The AST node that produced a function. Debugger tooling, stack trace mapping, and source-level stepping all benefit from knowing which `Lambda` or method node produced a given function entry.

**`FunctionEntry.RetSlots`** — Always 1. If multi-return functions are ever supported, this field carries the count.

**Why keep:** All are part of the `Bytecode` data model — the complete output of lowering. Removing them because no current consumer queries them treats a coherent output model as dead code. They cost nothing (nullable references, defaulted constructor parameters).

### Bytecode.Dump() / DumpToString() (54 lines) — KEEP

**What they do:** Format the µop list as human-readable text.

**Why keep:** Infrastructure for debugging and diagnostics. Debugging a miscompiled µop by reading the flat list is exactly when you reach for this. Adding it back later means rebuilding the formatting logic.

### Heap.UnsafeSet() (4 lines) — KEEP

**What it does:** Unsafe heap write without bounds checking. Symmetric counterpart of `UnsafeGet` (which is consumed by `CallSiteCompiler`).

**Why keep:** Part of the `Heap` concrete data type. If a hot path ever needs an unchecked write, it needs this exact method. 4 lines. Removing it breaks symmetry and saves nothing.

### InterpreterResult.None / Label / IsSignal (3 lines) — KEEP

**What they are:** `None` is a `Void` synonym; `Label` and `IsSignal` are computed properties on the result struct.

**Why keep:** Part of the `InterpreterResult` record's surface. Not worth removing from a coherent type for negligible line savings.

### ICollection<> on TypeDefinitionProviderCollection (40 lines) — KEEP

**What it does:** Implements `ICollection<ITypeDefinitionProvider>`, forcing `Contains`, `CopyTo`, `IsReadOnly`. No consumer calls these via the interface. `ProviderCount` duplicates `Count`.

**Why keep:** The interface signals "this is a collection" to the type system and forces `Count`, `Add`, `Remove`, `Clear` — which ARE used. The unused methods are interface compliance that works correctly. Not dead, just dormant.

### Never-emitted µop types (14 types) — KEEP

**What they are:** `NegImmOp` through `ShrImmOp` (9 immediate-bearing variants), `CmpLocalLeOp`/`CmpLocalJmpOp`, `IncLocalOp`, `BatchReduceOp`, `CountBitsOp`.

**Why keep:** The 9 imm variants exist in the `EmitBinary` switch and would be emitted if lowering's pattern detection covered them. The test/benchmark µops have active test coverage. All are part of the µop type hierarchy — removing them makes the abstraction incomplete.

### EmitDivRem with ModOp — KEEP AS IS

**What it does:** For `Modulo` operations, emits `DivRemOp` (computes both quotient and remainder) followed by `PopOp` (discards the quotient).

**Why keep:** A dedicated `ModOp` would save one µop per modulo operation, but creating a new µop type, wiring it into lowering, updating `ProgramCompiler`, and adding test coverage is measurable effort for a marginal gain. Not worth it unless modulo is a confirmed bottleneck.

### Domain-specific µops — KEEP

**What they are:** `StridedSetOp`, `CountBitsOp`, `BatchReduceOp`.

**Why keep:** These aren't domain-specific in the policy/modeling sense — they're performance-optimized fused loops for common numerical patterns. They're loop optimizations, not domain concepts leaking into the VM.
