# Neurosymbolic Platform — Specification

## Goal

Start a Poly-backed program and a local OCR model. The model, given enough time, codifies basic character recognition into algorithms and heuristics that run at native speed with zero inference cost.

## The Loop

```
Seed: ReturnOp (or any valid program)
  → Task: "character recognition" / "sort numbers" / etc.
  → Test suite: labeled examples (local, never crosses MCP)
  → Loop:
       Model or built-in mutations suggest changes
       Apply → evaluate against test suite → score
       Keep improvements, discard regressions
       Repeat until accuracy target or budget exhausted
  → Output: codified algorithm as IMacroMatcher — native speed
```

The model operates in two layers:

| Layer | Token space | Used for |
|---|---|---|
| AST | loops, conditions, blocks, lambdas, calls | Structural invention — new algorithm structures |
| µop | push, load, add, store, call | Optimization — sequence compression, fusion |

The model suggests both. Structural changes expand the search space (new algorithm shapes). µop-level changes contract it (shorter sequences). Domain modeling is one input channel for seed ASTs — the loop doesn't depend on it.

## MCP Contract

Messages are small — task strings, µop sequences (arrays of ints), scores.

```
Tool: suggest_algorithm_changes
  Input: {
    task: string,              // "character recognition"
    algorithm: int[],          // current µop sequence
    score: { accuracy, uopCount, stackDepth, elapsed },
    traceLevel?: "pc" | "stack" | "heap" | "full",  // optional debug trace
    examples?: Example[]       // 0-5 representative (input, output) pairs
  }
  Output: {
    uops: int[],               // candidate µop sequence
    intent: string,             // "added template matching"
    trace?: TraceEntry[]       // execution trace if requested
  }

Tool: report_result
  Input: { task, iterations, finalScore, macroCount }
  Output: {} (acknowledgment)
```

The test suite stays local. A few representative examples may cross MCP to illustrate the task. A domain-trained model (e.g., OCR) needs none — it already knows the mapping from training.

### Execution tracing

The model can request execution traces to debug its candidates:

```
Request: { task, uops, score, traceLevel: "stack" }
Response: {
  uops, intent,
  trace: [
    { pc: 0, uop: "loadarg 0", sp: 0, stack: [], heap: {} },
    { pc: 1, uop: "push 42", sp: 1, stack: [42], heap: {} },
    ...
  ]
}
```

Trace levels: `pc` (µop + stack height), `stack` (adds full stack contents), `heap` (adds heap state), `full` (adds locals, frame, everything).

## Scoring

| Dimension | How | Deterministic |
|---|---|---|
| µop count | `sequence.Length` | Yes |
| Stack pressure | Walk sequence, track max balance | Yes |
| Execution time | `Stopwatch` over N VM runs | No (noise) |

Hierarchy: **correctness → µop count → stack pressure → time**.

```
Score = (1 - accuracy) * 1e12  // accuracy is primary — wrong answers are infinitely bad
       + uopCount * 1e6
       + maxDepth * 1e3
       + elapsedNs
```

## UopRegistry — Pattern Discovery via Sliding Window

```csharp
record UopPattern {
    string Name;
    int[] MatchTypes;                // µop type IDs to match
    int MinFrequency;                // min occurrences to keep
    PatternKind Kind;                // Algorithm | Heuristic
    Func<int[], int[]>? Reduce;      // null = delete matched subsequence
}

class UopRegistry {
    void RegisterBuiltin(UopPattern);    // from known lowering patterns
    void DiscoverFrom(UopSequence);      // sliding-window frequency analysis
    UopSequence Optimize(UopSequence);   // apply all registered patterns
}
```

**Built-in patterns:** The 11 immediate-bearing fusions that already exist as µop types but lowering doesn't always emit: `[Push(v), Add]` → `AddImmOp(v)`, `[Push(v), Sub]` → `SubImmOp(v)`, etc.

Heuristics are lower-priority patterns with preconditions — they match only when a condition holds (e.g., loop bounds are small, array fits in cache). The registry separates exact algorithms (always apply) from heuristics (apply when beneficial).

## Phases

### Phase 0: UopRegistry + sliding-window analyzer (~300 lines)

- `UopPattern`, `UopRegistry`, `UopOptimizer`, `UopAnalyzer`
- Register the 11 known immediate-bearing fusion patterns
- Run on existing µop output — verify savings match expectations
- Run sliding-window discoverer — find repeated subsequences

**Visible output:**
```
Before: 142 µops (max depth: 8)
After:  131 µops (max depth: 8)
Saved:  11 µops (7.7%)

Discovered 3 candidate patterns:
  [LoadLocal, Dup] — appears 5 times, saves 5 µops
  [LoadArg, Push, Add] — appears 3 times, saves 6 µops
```

### Phase 1: MeasurementHarness + SynthesisDriver (~300 lines)

- `MeasurementHarness` — evaluate µop sequence against test suite
- `SynthesisDriver` — loop: mutate, evaluate, score, keep best
- Built-in µop-level mutations (no model needed)
- `VmState.MaxSteps` — timeout for runaway candidates

**Visible output:**
```
Iter  1: seed → 4 µops, accuracy: 0.0  (wrong)
Iter 10: 6 µops, accuracy: 0.0  (wrong)
Iter 35: 8 µops, accuracy: 1.0  (correct!)
Iter 60: 6 µops, accuracy: 1.0  (shorter)
Iter 90: 5 µops, accuracy: 1.0  (even shorter)
```

### Phase 2: MacroMatching + IPerceptionModule (~200 lines)

- `IMacroMatcher`, `MacroMatcherPipeline`
- `IPerceptionModule` interface + stub implementations
- Wire UopRegistry discoveries into MacroMatcherPipeline

**Visible output:**
```
Registered 3 new macros from sliding-window analysis:
  - [LoadLocal, Dup] → fused_macro_001 (hit 5x this run)
  - [LoadArg, Push, Add] → fused_macro_002 (hit 3x this run)
Lowering now emits fused µops for these patterns.
```

### Phase 3: MCP integration (~300 lines)

- MCP tools: `suggest_algorithm_changes`, `report_result`
- Connect to a real model
- Start with `ReturnOp`, model guides discovery toward the target task

### Phase 4: Training loop (~200 lines)

- Log all (state → action → outcome) transitions
- Train model on history — improves suggestion quality over time

## Implementation Order

### Batch 1 (start today)

```
Poly/Synthesis/UopPattern.cs            — record + sliding-window matching
Poly/Synthesis/UopRegistry.cs           — store patterns, Optimize()
Poly/Synthesis/UopAnalyzer.cs           — Discover() from frequency analysis
```

### Batch 2 (after UopRegistry works)

```
Poly/Synthesis/MeasurementHarness.cs    — evaluate against test suite via VM
Poly/Synthesis/SynthesisDriver.cs       — orchestration loop
Poly/Synthesis/MutationStrategies.cs    — built-in mutations
Poly/Interpretation/VirtualMachine/VmState.cs — MaxSteps property
Poly/Interpretation/VirtualMachine/ProgramCompiler.cs — emit step counter
```

### Batch 3 (after loop works)

```
Poly/Interpretation/VirtualMachine/MacroMatching/IMacroMatcher.cs
Poly/Interpretation/VirtualMachine/MacroMatching/MacroMatcherPipeline.cs
Poly/Synthesis/MacroLibrary.cs          — JSON persistence
Poly/Synthesis/IPerceptionModule.cs     — interface only
```

### Batch 4 (after codification works)

```
Poly.Mcp/SynthesisTools.cs              — MCP tools
```

**Total: ~1,100 lines across ~15 files. No new project references. No NuGet dependencies.**
