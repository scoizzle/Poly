# Micro-Task: Define the EvolutionTrace Record (Simple)

**Parent Workstream**: WS1 - Evolution Layer Core Infrastructure  
**Difficulty**: Small Model Friendly  
**Estimated Context**: < 4k tokens  
**Target Model Size**: Very Small / Small

## Objective
Define a clean, immutable `EvolutionTrace` record (and supporting simple types like `EvolutionStep`) that captures what happened during an evolution transaction.

## Context You Must Read First

1. Core Engineering Principles (focus on "build working code before abstraction").
2. The "Target Shape" and "Core Contract to Preserve" sections in `docs/decisions/2026-05-31-evolution-layer-design.md`.
3. Look at the existing `DomainMutationTrace` in V2 (`Poly/Data/Modeling/DomainMutationTrace.cs`) only for inspiration on fields — do **not** copy the implementation.

**Limit yourself to the files above + the V3 Domain model. Do not read the full port plan.**

## Exact Steps

1. Create a new file (suggested location: `Poly/DomainModeling/Evolution/EvolutionTrace.cs` or similar).

2. Define at minimum:
   ```csharp
   public sealed record EvolutionTrace(
       IReadOnlyList<EvolutionStep> Steps,
       IReadOnlyList<string> AffectedNodeIds,
       bool RolledBack,
       TimeSpan Duration,
       int ErrorCount,
       int WarningCount
   );

   public sealed record EvolutionStep(string ChangeDescription, IReadOnlyList<string> AffectedNodeIds);
   ```

3. Make the records immutable and simple (use `IReadOnlyList`).

4. Add basic factory methods if it makes usage clearer (e.g., `EvolutionTrace.Success(...)`, `EvolutionTrace.RolledBack(...)`).

5. Write a tiny test file that constructs a trace and verifies the fields.

## Verification Checklist

- [ ] Compiles cleanly
- [ ] Trace can be created for both success and rollback cases
- [ ] Small test passes
- [ ] The design is deliberately simple (no complex logic inside the trace itself)

## Output Expected

- New `EvolutionTrace.cs` (and `EvolutionStep.cs` if separate)
- One small test
- Mark this micro-task complete in the parent workstream file

## Status

**Claimed by**:  
**Status**: Not Started / In Progress / Done (summary submitted)

---

**Reminder**: After finishing, create a task summary in `../agent-summaries/` using the template. Do **not** edit the master roadmap or workstream files yourself. Keep this extremely simple. Follow the principle of building working code first.