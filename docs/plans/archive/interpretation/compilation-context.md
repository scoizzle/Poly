> **ARCHIVED (2026-07-10)** — Do not implement. Superseded by direct AST→VM-ABI (`DirectVmAbiEmitter`). See `docs/plans/archive/interpretation/README.md`.
>
> Original document follows for historical context only.

# Compilation Context — Implementation Plan

## Status: Active — 1159 passing, 20 failing, 0 skipped

## What's Complete

### Architecture
- `ProgramCompiler.Compile()` + `CompilationContext` — stateless context
- Per-µop `ValueSlot(pc)` — unique `_v{pc}` per instruction, no sharing
- `PopCount`/`PushCount` on every instruction type — producer resolver uses these
- `ConsumedFromPcs` — producer tracking with φ via `PhiSourcePcs`/`PhiAltPcs`
- `ResolveValue()` — φ resolves `Condition(ProgramCounter == srcPc, _v{alt}, _v{primary})`
- `ProgramCounter` set at Jump/BranchIfFalse for φ path identification
- Delegate preamble: `Registers ??= new long[max]`, `FrameBase = (== -1 ? 0)`, profiling init

### Files (16 + 28)
- `Vm/` — CompilationContext, ProgramCompiler, Vm, VmState, VmProgram, LoweringResult, ValueStack, Heap, Closure, CallSiteCompiler, VmTrace, SourceRange, FunctionEntry, Ref, Lowering
- `Vm/Instructions/` — 28 instruction types, each per-file with ABI docs

### Deleted
- `TempVar.cs`, old `VirtualMachine/`, `Bytecode.cs`, `ProgramCompiler2.cs`, `InstructionMetadataStore.cs`, `RegisterAllocator.cs`, `MemberHelper.cs`

## Remaining Work

### Failures (all through stub lowering)
| Count | Test | Symptom |
|-------|------|---------|
| 2 | Triangular_10/100 | null result — WhileLoop accumulator not tracked |
| 3 | CountPrimes_10/100/1000 | found 1 — inner WhileLoop/Conditional logic |
| 1 | Mandelbrot_128 | found 1 — complex nested loops |
| 1 | ClrMaxChain_50 | Functions empty — Call instruction not emitted |
| 1 | DeepSum_5000 | null result — WhileLoop accumulator |
| 2 | Reverse_100/123 | found 0 — WhileLoop digit extraction |
| 2 | CountDigits_0/12345 | found 0 — WhileLoop digit counting |
| 2 | Fact_0/1/5/10 | found 0 — test uses a WhileLoop? |
| 4 | Power_2/3, SumSquares, Gcd, Fib, Collatz | all through stub lowering |
| 2 | Triangular_10/100, DeepSum | null result from WhileLoop |
| 1 | ClrMaxChain_50 | HandleCall with empty Functions array |

### Next steps
1. Port full lowering from `VirtualMachine.old/Lowering.cs` reference
2. Implement proper WhileLoop/IfStatement/Conditional with live variable tracking
3. Implement function call lowering with `Call` instruction + function index metadata
4. Re-enable `Depth == 0` assertion with proper basic-block-aware analysis

### Verification
```
dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj
dotnet run --project Poly.Tests/Poly.Tests.csproj
```
