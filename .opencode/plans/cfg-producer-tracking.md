# CFG-Based Producer Tracking — Fix Plan

## Root Cause

`ResolveProducers` uses a forward linear walk with a `Stack<int>` of producer PCs. It processes ALL instructions sequentially — including µops from untaken branches (e.g., `LoadConst(0)` on the if-true path when the if-false path is taken). This pollutes the stack. The φ pass patches only ONE alternate path but the primary consumed values are already wrong.

## Fix: Backwards CFG Traversal

Instead of a single forward pass, trace each predecessor path independently from each consuming µop.

### Step 1: Build predecessor graph

```csharp
// For each PC, list of PCs that can reach it
var predecessors = new List<int>[n];
for (int pc = 0; pc < n; pc++) {
    var op = instructions[pc];
    // Linear fallthrough
    if (pc + 1 < n)
        predecessors[pc + 1].Add(pc);
    // Jump
    if (op is Jump jmp)
        predecessors[jmp.Target].Add(pc);
    // BranchIfFalse: both target and fallthrough
    if (op is BranchIfFalse bif) {
        predecessors[bif.Target].Add(pc);
        if (pc + 1 < n)
            predecessors[pc + 1].Add(pc);  // already added above, but explicit
    }
}
```

### Step 2: Walk µops forward, tracking entry stacks per predecessor

For each µop, save the producer stack as it enters the µop. But instead of a single stack, maintain a `Dictionary<int, int[]>` mapping each predecessor PC to its entry stack. For convergence points (multiple predecessors), compare stacks.

This is still a forward pass but with predecessor-aware stack tracking:

```csharp
// entryStackAt[pc, predPc] = stack at entry to pc from predecessor predPc
var entryStacks = new Dictionary<(int Pc, int Pred), int[]>();

for (int pc = 0; pc < n; pc++) {
    // Look up which predecessors were computed from branches
    var preds = predecessors[pc];
    if (preds.Count == 0) {
        // Entry point — empty stack
        entryStacks[(pc, -1)] = [];
    } else if (preds.Count == 1) {
        // Single predecessor — carry forward its exit stack
        int pred = preds[0];
        entryStacks[(pc, pred)] = ComputeExitStack(pred);
    } else {
        // Multiple predecessors — save each one's exit stack
        foreach (var pred in preds)
            entryStacks[(pc, pred)] = ComputeExitStack(pred);
    }
    
    // Now compute exit stack for this µop
    // ...
}
```

### Step 3: Compute exit stack per µop

A µop's exit stack = entry stack - PopCount + PushCount entries.

For a µop with entry stack S and PopCount p, PushCount q:
```
Consumed = S[Depth - p .. Depth - 1]  (top p values)
Exit = S[0 .. Depth - p - 1] ++ [pc, pc, ...]  (remove p, add q new PCs)
```

### Step 4: φ at convergence

For a µop reached from multiple predecessors, each predecessor provides a different entry stack. At the µop itself, the consumed values from each path are computed independently. Where they differ, φ is set.

```csharp
for (int pc = 0; pc < n; pc++) {
    var preds = predecessors[pc];
    if (preds.Count < 2) continue;
    
    var op = instructions[pc];
    int popCount = op.PopCount;
    if (popCount == 0) continue;
    
    // Compute consumed values from each predecessor
    var consumedByPred = new Dictionary<int, int[]>();
    foreach (var pred in preds) {
        var entryStack = entryStacks[(pc, pred)];
        var consumed = new int[popCount];
        for (int i = popCount - 1; i >= 0; i--)
            consumed[i] = entryStack[entryStack.Length - popCount + i];
        consumedByPred[pred] = consumed;
    }
    
    // Compare: use first as primary, detect differences
    var primary = consumedByPred[preds[0]];
    bool needsPhi = false;
    foreach (var (pred, alt) in consumedByPred) {
        for (int i = 0; i < popCount; i++)
            if (primary[i] != alt[i]) { needsPhi = true; break; }
        if (needsPhi) {
            // Set φ: primary is from first pred, alt from this pred
            instructions[pc] = op with {
                ConsumedFromPcs = primary,
                PhiSourcePcs = Enumerable.Repeat(pred, popCount).ToArray(),
                PhiAltPcs = alt
            };
            break;
        }
    }
}
```

### Step 5: Compilation phase

In `ProgramCompiler.Compile()`, the `ResolveValue` method already handles φ via `Condition(ProgramCounter == srcPc, alt, primary)`. No changes needed there — the Instruction data model (`PhiSourcePcs`/`PhiAltPcs`) already supports this. Only the producer detection needs fixing.

## Files to change

| File | Change |
|------|--------|
| `Vm/ProgramCompiler.cs` | Replace `ResolveProducers` with CFG-based version |
| `Vm/CompilationContext.cs` | No change — `ResolveValue` already handles φ |

## Verification

The fix should resolve all 5 failing tests:
- CountPrimes_10/100/1000 — conditional convergence in nested loop
- Mandelbrot_128_Compare — nested loops with conditionals
- ClrMaxChain_50 — requires additional `CallSite` plumbing (separate issue)

Expected progression: 1174 → 1177+ passing.
