# ADR: Peephole Optimizer — Post-Lowering Pass

**Date:** 2026-06-08  
**Status:** Accepted  

## Context

The lowering pass maps AST nodes directly to opcode sequences with no optimization. This produces correct but bloated bytecode. Common patterns include:

- `PushInt 0; Add` (identity — value unchanged)
- `PushInt 0; Mul` (zero — result is always 0)
- `PushInt 1; Mul` (identity)
- `PushInt 0; Sub` (identity)
- `PushInt 0; Div` (undefined, but could emit `PushInt 0` directly since 0/x = 0 for x≠0)
- `Jump L1; L1: ...` (unnecessary jump)
- `JumpIfFalse L1; Jump L2; L1: ...; L2: ...` (inverted branch target)
- `Not; JumpIfFalse L1` → `JumpIfTrue L1` (absent a JumpIfTrue opcode)
- Consecutive `Pop` instructions from block statement cleanup

These patterns are easy to detect in a single linear pass and removing them reduces bytecode size and execution steps proportionally.

## Decision

Add a post-lowering peephole optimizer pass. The pass operates directly on the `byte[]` instruction stream after `Lower()` returns.

### Contract

1. **The optimizer is a `static Bytecode Optimize(Bytecode input)` method** in a new file `Optimizer.cs` under `Poly/Interpretation/VirtualMachine/`.

2. **It performs a single forward pass** over the bytecode, matching fixed-size patterns and emitting replacement sequences.

3. **Pattern detection is expression-switch based** on the current opcode and its immediate operands.

4. **Jump targets are adjusted** when instructions are removed or shortened. The optimizer maintains a `Dictionary<int, int>` mapping old PC → new PC and patches all jump operands and exception regions in a second pass.

5. **The optimizer is optional.** `Lower()` does not call it automatically. The caller (test or production pipeline) decides whether to optimize.

### Initial pattern set

| Pattern | Replacement | Savings |
|---------|-------------|---------|
| `PushInt 0; Add` | (remove both) | 6 bytes, 1 step |
| `PushInt 0; Sub` | (remove both) | 6 bytes, 1 step |
| `PushInt 1; Mul` | (remove both) | 6 bytes, 1 step |
| `PushInt 0; Mul` | `PushInt 0` (remove Mul) | 1 byte, 1 step |
| `PushInt 0; Div` | `PushInt 0` (remove Div; 0/x=0) | 1 byte, 1 step |
| `Jump L; L:` | (remove Jump) | 5 bytes, 1 step |
| `Not; JumpIfFalse L` | `JumpIfTrue L` (new opcode) | 0 bytes, 1 step |
| `Dup; Pop` | (remove both) | 2 bytes, 2 steps |
| `Pop; Pop` | (leave one Pop) | 1 byte, 1 step |

### Sketch

```csharp
internal static class Optimizer {
    public static Bytecode Optimize(Bytecode input) {
        var code = input.Code;
        var output = new List<byte>(code.Length);
        var pcMap = new Dictionary<int, int>(); // old → new

        int i = 0;
        while (i < code.Length) {
            pcMap[i] = output.Count;
            int next = TryFoldPattern(code, i, output);
            if (next > i) { i = next; continue; }
            // Copy instruction as-is
            int len = InstructionLength(code, i);
            output.AddRange(code.AsSpan(i, len));
            i += len;
        }

        // Patch jumps and exception regions
        var patchedCode = PatchTargets([.. output], pcMap);
        var patchedRegions = PatchExceptionRegions(input.ExceptionRegions, pcMap);
        return new Bytecode(patchedCode, input.SourceMap, input.Functions,
            input.Constants, input.CallSites, patchedRegions, input.ResultType);
    }
}
```

## Rationale

- No analysis context required — operates directly on produced bytecode.
- Single pass, linear time.
- Catches the most common bloat patterns from naive lowering.
- Does not change semantics — verified by running the conformance suite on optimized output.
- Optional, so it can be enabled incrementally as patterns are validated.

## Consequences

- `Optimizer.cs` is added under `Interpretation/VirtualMachine/`.
- `JumpIfTrue` opcode may be added if the `Not; JumpIfFalse` pattern is common enough to warrant it (or the pattern just becomes `Not; Not; JumpIfFalse` → `JumpIfFalse`).
- Jump target patching is the most error-prone part — the `pcMap` must correctly map every old PC to its new position, including within multi-byte instructions.
- The conformance suite should run both optimized and unoptimized paths.