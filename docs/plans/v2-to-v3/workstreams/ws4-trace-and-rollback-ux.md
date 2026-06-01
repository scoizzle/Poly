# Workstream WS4: Trace & Agent Experience Quality (Rollback UX)

**Phase**: 1  
**Priority**: High  
**Owner**: TBD  
**Status**: In Progress (orchestrator-led kickoff after WS7 completion)  
**Last Updated**: 2026-06 (full-send simplification pass: removed overall AffectedNodeIds + AttemptedChanges; verified clean Information diagnostic integration)

## Goal
Ensure that the traces and rollback behavior produced by the evolution layer are high-quality, clear, and genuinely useful for LLM/MCP agents — at least as good as (ideally better than) the old V2 `ApplyWithTrace` experience.

## Entry Criteria
- WS1 foundation complete (usable trace shape + per-step NodeIds in place) — Done.
- WS7 expressiveness audit complete — Done (June 2026).

WS4 is now active.

## Key Areas to Improve

- Richness and readability of `EvolutionTrace` and `EvolutionStep`
- Quality of diagnostics when rollback occurs
- Consistency of step descriptions across different operation types
- Usefulness for agents trying to understand "what just happened and why did it fail?"
- Performance characteristics of trace generation (should not be expensive)

## Suggested Initial Tasks (to be turned into micro-tasks)

1. Define a good `EvolutionStep` format and description convention.
2. Improve the rollback path to produce clear, actionable error information.
3. Add support for attaching relevant code snippets or context to steps where helpful.
4. Create good test coverage for trace output in both success and rollback scenarios.
5. Document "how agents should interpret traces" for future users.

## Exit Criteria
- Traces from complex multi-step evolutions (including rollbacks) are clear and actionable.
- At least one real roadblock scenario produces excellent trace output.
- Clear guidelines exist for what good vs. poor trace output looks like.
- Integration with WS5 (proof on examples) is clean.

## Dependencies
- WS1 (core trace generation)
- WS3 (operations to exercise the traces)

## Parallelism Notes
This work can start as soon as WS1 has basic trace output. It can run in parallel with further operation implementation in WS3. Good candidates for smaller agents once the core trace shape is stable.

## Related Documents
- `docs/decisions/2026-05-31-evolution-layer-design.md` (Core Contract to Preserve section + Trace fidelity question)
- EvolutionTrace and BuildTrace implementation in `Poly/DomainModeling/Evolution/`

## Current Status (June 2026) — Honest Reassessment

After aggressive experimentation, we reached an important clarity:

The custom error text generation we were building in `EvolutionTrace` (`TopErrorMessages`, `PrimaryFailureMessage`, ad-hoc prefixes, embedded "Attempted changes" strings in error lists, etc.) was overly complicated and added less value than the existing `AnalysisResult.Diagnostics` on `EvolutionResult`, which are already the authoritative, structured source of error information.

**Corrected (and better) direction:**

The user's observation is key: **There is nothing stopping change history from being informational diagnostics.**

The cleanest, most powerful model is:

- Each step in the evolution (each `DomainChange`) can/should be reported as a `DiagnosticSeverity.Information` diagnostic.
- This makes the entire change history part of the unified diagnostic stream that agents already consume.
- `EvolutionTrace` then becomes a convenient *structured view* / projection over the informational diagnostics + the raw change data, rather than a parallel error-reporting mechanism.
- Real errors remain in the proper `Error`/`Warning` diagnostics. Change history becomes first-class `Information` diagnostics.

This is higher fidelity, less duplication, and aligns with how the rest of the system (analyzers, `AnalysisContext.ReportInformation`, etc.) already works.

**What we kept / built (ultra-lean model):**
- Single high-quality natural-language `ChangeDescription` per step (owned by each `DomainChange` subtype — no central switch).
- Step history emitted as first-class `DiagnosticSeverity.Information` diagnostics with code `"EVOLUTION_STEP"` (unified diagnostic model; no parallel custom error text or duplicate description concepts).
- `Duration`, `ErrorCount`, `WarningCount` on the trace as cheap at-a-glance signals.
- `EvolutionTrace` and `EvolutionStep` reduced to the minimal useful shape (Steps list + success/rollback signal + timing + counts). No `AffectedNodeIds` (overall or per-step), no `AttemptedChanges` (redundant with Steps), no `Details`, no `ChangeKind`.

**What we simplified / removed (aggressive full-send cleanup):**
- Custom error text fields in the trace (`TopErrorMessages`, `PrimaryFailureMessage`, etc.) removed — diagnostics are authoritative.
- `GetDetails()` / `Details` concept removed entirely (duplicated the description string).
- `ChangeKind` / `GetChangeKind` removed (description already communicates the operation clearly and in better prose).
- Per-step `AffectedNodeIds` removed (unnecessary allocation and examination cost).
- Overall `AffectedNodeIds` tracking removed from `DomainMutationContext`, all `DomainChange.ApplyTo` implementations, `EvolutionTrace`, and `BuildTrace` (hot-path cleanup; incremental analysis still receives empty for MVP; no real consumer of the list on the V3 trace yet).
- Redundant `AttemptedChanges` string list removed (Steps already provide the ordered descriptions).
- The informational diagnostics injection timing was corrected so EVOLUTION_STEP infos are present in the materialized `AnalysisResult.Diagnostics` for both success and rejection paths.

The trace is now the minimal high-fidelity structured view: ordered step descriptions (also flowing as Information diagnostics) + rollback signal + timing + error/warning counts. All duplicate concepts and "examine nodes for trace" work eliminated.

**Current state (full-send complete for this iteration):**
- Central switch for descriptions eliminated long ago; each `DomainChange` owns `GetDescription()`.
- Change history is first-class Information diagnostics (verified in rejection test with real assertions on `EVOLUTION_STEP` diags).
- `EvolutionTrace` / `EvolutionStep` are ultra-lean.
- All tests green. The model is honest, allocation-efficient in the mutation path, and excellent for agents (unified diagnostics + lightweight trace projection).